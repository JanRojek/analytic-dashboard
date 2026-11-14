using System.Data;

using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ImportJobRepository : IImportJobRepository
{
    private readonly AppDbContext _dbContext;
    private readonly IProjectWriteTransactionFactory _transactionFactory;

    public ImportJobRepository(
        AppDbContext dbContext,
        IProjectWriteTransactionFactory transactionFactory)
    {
        _dbContext = dbContext;
        _transactionFactory = transactionFactory;
    }

    public async Task<ImportJobWorkItem?> ClaimNextPendingAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        // Keep the AUD-001 lock order: project before job; skip busy projects instead of waiting.
        var claimedJobs = await _dbContext.ImportJobs
            .FromSqlInterpolated($"""
                WITH candidate_project AS MATERIALIZED (
                    SELECT project."Id"
                    FROM import_jobs AS pending_job
                    JOIN dataset_versions AS version ON version."Id" = pending_job."DatasetVersionId"
                    JOIN datasets AS dataset ON dataset."Id" = version."DatasetId"
                    JOIN projects AS project ON project."Id" = dataset."ProjectId"
                    WHERE pending_job."Status" = {ImportJobStatus.Pending.ToString()}
                    ORDER BY pending_job."CreatedAtUtc", pending_job."Id"
                    LIMIT 1
                    FOR NO KEY UPDATE OF project SKIP LOCKED
                ),
                candidate_job AS MATERIALIZED (
                    SELECT pending_job."Id"
                    FROM import_jobs AS pending_job
                    JOIN dataset_versions AS version ON version."Id" = pending_job."DatasetVersionId"
                    JOIN datasets AS dataset ON dataset."Id" = version."DatasetId"
                    JOIN candidate_project AS project ON project."Id" = dataset."ProjectId"
                    WHERE pending_job."Status" = {ImportJobStatus.Pending.ToString()}
                    ORDER BY pending_job."CreatedAtUtc", pending_job."Id"
                    LIMIT 1
                    FOR UPDATE OF pending_job SKIP LOCKED
                )
                UPDATE import_jobs AS job
                SET "Status" = {ImportJobStatus.Running.ToString()},
                    "StartedAtUtc" = clock_timestamp(),
                    "AttemptNumber" = job."AttemptNumber" + 1
                FROM candidate_job AS candidate
                WHERE job."Id" = candidate."Id"
                  AND job."Status" = {ImportJobStatus.Pending.ToString()}
                RETURNING job.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var job = claimedJobs.SingleOrDefault();
        if (job is null)
        {
            return null;
        }

        var trackedJob = _dbContext.ImportJobs.Local.SingleOrDefault(item => item.Id == job.Id);
        if (trackedJob is null)
        {
            _dbContext.ImportJobs.Attach(job);
        }
        else
        {
            await _dbContext.Entry(trackedJob).ReloadAsync(cancellationToken);
            job = trackedJob;
        }

        var version = await _dbContext.DatasetVersions.SingleAsync(
            version => version.Id == job.DatasetVersionId,
            cancellationToken
        );
        var dataset = await _dbContext.Datasets.SingleAsync(
            dataset => dataset.Id == version.DatasetId,
            cancellationToken
        );

        await transaction.CommitAsync(cancellationToken);

        return new ImportJobWorkItem(
            job,
            version,
            dataset,
            job.AttemptNumber
        );
    }

    public async Task<ImportJobCompletionOutcome> CompleteAsync(
        ImportJobWorkItem workItem,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _transactionFactory.BeginAsync(
            workItem.Dataset.ProjectId,
            cancellationToken
        );

        if (transaction is null)
        {
            return ImportJobCompletionOutcome.OwnershipLost;
        }

        var job = await GetOwnedRunningJobAsync(workItem, cancellationToken);
        if (job is null)
        {
            var completed = await IsCompletedAsync(
                workItem,
                storageKey,
                rowCount,
                columnCount,
                cancellationToken
            );

            return completed
                ? ImportJobCompletionOutcome.AlreadyCompleted
                : ImportJobCompletionOutcome.OwnershipLost;
        }

        var version = await _dbContext.DatasetVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == job.DatasetVersionId, cancellationToken);

        version.MarkReady(storageKey, rowCount, columnCount);
        job.MarkCompleted();

        var versionChanges = await _dbContext.DatasetVersions
            .Where(item => item.Id == version.Id && item.Status == DatasetVersionStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.StorageKey, version.StorageKey)
                    .SetProperty(item => item.RowCount, version.RowCount)
                    .SetProperty(item => item.ColumnCount, version.ColumnCount)
                    .SetProperty(item => item.Status, version.Status),
                cancellationToken
            );

        var datasetChanges = await _dbContext.Datasets
            .Where(dataset => dataset.Id == version.DatasetId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(dataset => dataset.CurrentVersionId, version.Id),
                cancellationToken
            );

        var jobChanges = await _dbContext.ImportJobs
            .Where(item =>
                item.Id == job.Id
                && item.AttemptNumber == workItem.AttemptNumber
                && item.Status == ImportJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, job.Status)
                    .SetProperty(item => item.CompletedAtUtc, job.CompletedAtUtc),
                cancellationToken
            );

        if (versionChanges != 1 || datasetChanges != 1 || jobChanges != 1)
        {
            throw new InvalidOperationException("Import completion did not update all required records.");
        }

        await transaction.CommitAsync(cancellationToken);

        return ImportJobCompletionOutcome.Completed;
    }

    public Task<bool> IsCompletedAsync(
        ImportJobWorkItem workItem,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken)
    {
        // A later import may already be current; recognize this publication by its immutable version and artifact.
        return (
            from job in _dbContext.ImportJobs.AsNoTracking()
            join version in _dbContext.DatasetVersions.AsNoTracking()
                on job.DatasetVersionId equals version.Id
            where job.Id == workItem.Job.Id
                && job.AttemptNumber == workItem.AttemptNumber
                && job.Status == ImportJobStatus.Completed
                && version.Status == DatasetVersionStatus.Ready
                && version.StorageKey == storageKey
                && version.RowCount == rowCount
                && version.ColumnCount == columnCount
            select job.Id
        ).AnyAsync(cancellationToken);
    }

    public async Task<bool> TryFailAsync(
        ImportJobWorkItem workItem,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _transactionFactory.BeginAsync(
            workItem.Dataset.ProjectId,
            cancellationToken
        );

        if (transaction is null)
        {
            return false;
        }

        var job = await GetOwnedRunningJobAsync(workItem, cancellationToken);
        if (job is null)
        {
            return false;
        }

        var version = await _dbContext.DatasetVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == job.DatasetVersionId, cancellationToken);

        version.MarkFailed();
        job.MarkFailed(errorMessage);

        var versionChanges = await _dbContext.DatasetVersions
            .Where(item => item.Id == version.Id && item.Status == DatasetVersionStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.Status, version.Status),
                cancellationToken
            );

        var jobChanges = await _dbContext.ImportJobs
            .Where(item =>
                item.Id == job.Id
                && item.AttemptNumber == workItem.AttemptNumber
                && item.Status == ImportJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, job.Status)
                    .SetProperty(item => item.ErrorMessage, job.ErrorMessage)
                    .SetProperty(item => item.CompletedAtUtc, job.CompletedAtUtc),
                cancellationToken
            );

        if (versionChanges != 1 || jobChanges != 1)
        {
            throw new InvalidOperationException("Import failure did not update all required records.");
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<ImportJob?> GetLatestByDatasetIdAsync(
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var datasetVersionIds = _dbContext.DatasetVersions
            .Where(version => version.DatasetId == datasetId)
            .Select(version => version.Id);

        return await _dbContext.ImportJobs
            .AsNoTracking()
            .Where(job => datasetVersionIds.Contains(job.DatasetVersionId))
            .OrderByDescending(job => job.CreatedAtUtc)
            .ThenByDescending(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImportJob>> GetRunningAsync(
        CancellationToken cancellationToken)
    {
        return await _dbContext.ImportJobs
            .Where(job => job.Status == ImportJobStatus.Running)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(
            cancellationToken
        );
    }

    private async Task<ImportJob?> GetOwnedRunningJobAsync(
        ImportJobWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var jobs = await _dbContext.ImportJobs
            .FromSqlInterpolated($"""
                SELECT job.*
                FROM import_jobs AS job
                WHERE job."Id" = {workItem.Job.Id}
                  AND job."AttemptNumber" = {workItem.AttemptNumber}
                  AND job."Status" = {ImportJobStatus.Running.ToString()}
                FOR UPDATE OF job
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return jobs.SingleOrDefault();
    }
}
