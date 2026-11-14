using System.Data;

using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ImportJobRepository : IImportJobRepository
{
    // "APERTURE": all instances must use the same owner claim lock namespace.
    private const long OwnerClaimLockSeed = 0x4150455254555245;

    private readonly AppDbContext _dbContext;
    private readonly IProjectWriteTransactionFactory _transactionFactory;
    private readonly ImportJobOptions _options;

    public ImportJobRepository(
        AppDbContext dbContext,
        IProjectWriteTransactionFactory transactionFactory,
        IOptions<ImportJobOptions> options)
    {
        _dbContext = dbContext;
        _transactionFactory = transactionFactory;
        _options = options.Value;
    }

    public async Task<ImportJobWorkItem?> ClaimNextPendingAsync(
        CancellationToken cancellationToken)
    {
        var skippedOwners = new List<Guid>();

        while (true)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            var owners = await (
                from pendingJob in _dbContext.ImportJobs.AsNoTracking()
                join pendingVersion in _dbContext.DatasetVersions on pendingJob.DatasetVersionId equals pendingVersion.Id
                join pendingDataset in _dbContext.Datasets on pendingVersion.DatasetId equals pendingDataset.Id
                join project in _dbContext.Projects on pendingDataset.ProjectId equals project.Id
                where pendingJob.Status == ImportJobStatus.Pending
                    && pendingJob.AttemptNumber < _options.MaxAttempts
                    && !skippedOwners.Contains(project.OwnerId)
                    && !(from runningJob in _dbContext.ImportJobs
                         join runningVersion in _dbContext.DatasetVersions on runningJob.DatasetVersionId equals runningVersion.Id
                         join runningDataset in _dbContext.Datasets on runningVersion.DatasetId equals runningDataset.Id
                         join runningProject in _dbContext.Projects on runningDataset.ProjectId equals runningProject.Id
                         where runningJob.Status == ImportJobStatus.Running && runningProject.OwnerId == project.OwnerId
                         select runningJob.Id).Any()
                orderby pendingJob.CreatedAtUtc, pendingJob.Id
                select project.OwnerId
            ).Take(1).ToListAsync(cancellationToken);

            if (owners.Count == 0)
            {
                return null;
            }

            var ownerId = owners[0];
            skippedOwners.Add(ownerId);
            var ownerLocks = await _dbContext.Database.SqlQuery<bool>($"""
                SELECT pg_try_advisory_xact_lock(hashtextextended({ownerId}::text, {OwnerClaimLockSeed})) AS "Value"
                """).ToListAsync(cancellationToken);

            if (!ownerLocks.Single())
            {
                continue;
            }

            // Recheck Running in a fresh READ COMMITTED statement after the owner lock, then lock project before job.
            var claimedJobs = await _dbContext.ImportJobs
                .FromSqlInterpolated($"""
                    WITH candidate_project AS MATERIALIZED (
                        SELECT project."Id"
                        FROM import_jobs AS pending_job
                        JOIN dataset_versions AS version ON version."Id" = pending_job."DatasetVersionId"
                        JOIN datasets AS dataset ON dataset."Id" = version."DatasetId"
                        JOIN projects AS project ON project."Id" = dataset."ProjectId"
                        WHERE pending_job."Status" = {ImportJobStatus.Pending.ToString()}
                          AND pending_job."AttemptNumber" < {_options.MaxAttempts}
                          AND project."OwnerId" = {ownerId}
                          AND NOT EXISTS (
                              SELECT 1
                              FROM import_jobs AS running_job
                              JOIN dataset_versions AS running_version ON running_version."Id" = running_job."DatasetVersionId"
                              JOIN datasets AS running_dataset ON running_dataset."Id" = running_version."DatasetId"
                              JOIN projects AS running_project ON running_project."Id" = running_dataset."ProjectId"
                              WHERE running_job."Status" = {ImportJobStatus.Running.ToString()}
                                AND running_project."OwnerId" = {ownerId}
                          )
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
                          AND pending_job."AttemptNumber" < {_options.MaxAttempts}
                        ORDER BY pending_job."CreatedAtUtc", pending_job."Id"
                        LIMIT 1
                        FOR UPDATE OF pending_job SKIP LOCKED
                    )
                    UPDATE import_jobs AS job
                    SET "Status" = {ImportJobStatus.Running.ToString()},
                        "StartedAtUtc" = clock_timestamp(),
                        "LeaseExpiresAtUtc" = clock_timestamp() + {_options.LeaseDuration},
                        "CompletedAtUtc" = NULL,
                        "ErrorMessage" = NULL,
                        "AttemptNumber" = job."AttemptNumber" + 1
                    FROM candidate_job AS candidate
                    WHERE job."Id" = candidate."Id"
                      AND job."Status" = {ImportJobStatus.Pending.ToString()}
                      AND job."AttemptNumber" < {_options.MaxAttempts}
                    RETURNING job.*
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var job = claimedJobs.SingleOrDefault();
            if (job is null)
            {
                continue;
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
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
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
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
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

    public async Task<bool> TryRenewLeaseAsync(
        ImportJobWorkItem workItem,
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

        var changed = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_jobs
            SET "LeaseExpiresAtUtc" = clock_timestamp() + {_options.LeaseDuration}
            WHERE "Id" = {workItem.Job.Id}
              AND "AttemptNumber" = {workItem.AttemptNumber}
              AND "Status" = {ImportJobStatus.Running.ToString()}
              AND "LeaseExpiresAtUtc" > clock_timestamp()
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return changed == 1;
    }

    public async Task<bool> TryAbandonAsync(
        ImportJobWorkItem workItem,
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

        // Retain a short cooldown; recovery remains responsible for retry and the attempt limit.
        var changed = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_jobs
            SET "LeaseExpiresAtUtc" = LEAST("LeaseExpiresAtUtc", clock_timestamp() + {_options.HeartbeatInterval})
            WHERE "Id" = {workItem.Job.Id}
              AND "AttemptNumber" = {workItem.AttemptNumber}
              AND "Status" = {ImportJobStatus.Running.ToString()}
              AND "LeaseExpiresAtUtc" > clock_timestamp()
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return changed == 1;
    }

    public async Task<bool> RecoverNextExpiredAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        var recovered = await _dbContext.ImportJobs
            .FromSqlInterpolated($"""
                WITH candidate_project AS MATERIALIZED (
                    SELECT project."Id"
                    FROM import_jobs AS expired_job
                    JOIN dataset_versions AS version ON version."Id" = expired_job."DatasetVersionId"
                    JOIN datasets AS dataset ON dataset."Id" = version."DatasetId"
                    JOIN projects AS project ON project."Id" = dataset."ProjectId"
                    WHERE (expired_job."Status" = {ImportJobStatus.Running.ToString()}
                           AND (expired_job."LeaseExpiresAtUtc" IS NULL OR expired_job."LeaseExpiresAtUtc" <= clock_timestamp()))
                       OR (expired_job."Status" = {ImportJobStatus.Pending.ToString()}
                           AND expired_job."AttemptNumber" >= {_options.MaxAttempts})
                    ORDER BY expired_job."LeaseExpiresAtUtc" NULLS FIRST, expired_job."Id"
                    LIMIT 1
                    FOR NO KEY UPDATE OF project SKIP LOCKED
                ),
                candidate_job AS MATERIALIZED (
                    SELECT expired_job."Id"
                    FROM import_jobs AS expired_job
                    JOIN dataset_versions AS version ON version."Id" = expired_job."DatasetVersionId"
                    JOIN datasets AS dataset ON dataset."Id" = version."DatasetId"
                    JOIN candidate_project AS project ON project."Id" = dataset."ProjectId"
                    WHERE (expired_job."Status" = {ImportJobStatus.Running.ToString()}
                           AND (expired_job."LeaseExpiresAtUtc" IS NULL OR expired_job."LeaseExpiresAtUtc" <= clock_timestamp()))
                       OR (expired_job."Status" = {ImportJobStatus.Pending.ToString()}
                           AND expired_job."AttemptNumber" >= {_options.MaxAttempts})
                    ORDER BY expired_job."LeaseExpiresAtUtc" NULLS FIRST, expired_job."Id"
                    LIMIT 1
                    FOR UPDATE OF expired_job SKIP LOCKED
                )
                UPDATE import_jobs AS job
                SET "Status" = CASE WHEN job."AttemptNumber" >= {_options.MaxAttempts}
                                    THEN {ImportJobStatus.Failed.ToString()} ELSE {ImportJobStatus.Pending.ToString()} END,
                    "LeaseExpiresAtUtc" = NULL,
                    "StartedAtUtc" = CASE WHEN job."AttemptNumber" >= {_options.MaxAttempts}
                                         THEN job."StartedAtUtc" ELSE NULL END,
                    "CompletedAtUtc" = CASE WHEN job."AttemptNumber" >= {_options.MaxAttempts}
                                           THEN clock_timestamp() ELSE NULL END,
                    "ErrorMessage" = CASE WHEN job."AttemptNumber" >= {_options.MaxAttempts}
                                          THEN 'Import attempt limit reached after worker lease expired.' ELSE NULL END
                FROM candidate_job AS candidate
                WHERE job."Id" = candidate."Id"
                  AND ((job."Status" = {ImportJobStatus.Running.ToString()}
                        AND (job."LeaseExpiresAtUtc" IS NULL OR job."LeaseExpiresAtUtc" <= clock_timestamp()))
                       OR (job."Status" = {ImportJobStatus.Pending.ToString()}
                           AND job."AttemptNumber" >= {_options.MaxAttempts}))
                RETURNING job.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var job = recovered.SingleOrDefault();
        if (job is null)
        {
            return false;
        }

        if (job.Status == ImportJobStatus.Failed)
        {
            var changed = await _dbContext.DatasetVersions
                .Where(version => version.Id == job.DatasetVersionId && version.Status == DatasetVersionStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(version => version.Status, DatasetVersionStatus.Failed),
                    cancellationToken
                );

            if (changed != 1)
            {
                throw new InvalidOperationException("Expired import did not have a pending dataset version.");
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
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
                  AND job."LeaseExpiresAtUtc" > clock_timestamp()
                FOR UPDATE OF job
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return jobs.SingleOrDefault();
    }
}
