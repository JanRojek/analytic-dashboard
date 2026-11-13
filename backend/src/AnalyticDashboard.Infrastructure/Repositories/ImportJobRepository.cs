using System.Data;

using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ImportJobRepository : IImportJobRepository
{
    private readonly AppDbContext _dbContext;

    public ImportJobRepository(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
}
