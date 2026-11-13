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
        var candidate = await (
            from pendingJob in _dbContext.ImportJobs.AsNoTracking()
            join pendingVersion in _dbContext.DatasetVersions.AsNoTracking()
                on pendingJob.DatasetVersionId equals pendingVersion.Id
            join pendingDataset in _dbContext.Datasets.AsNoTracking()
                on pendingVersion.DatasetId equals pendingDataset.Id
            where pendingJob.Status == ImportJobStatus.Pending
            orderby pendingJob.CreatedAtUtc, pendingJob.Id
            select new
            {
                JobId = pendingJob.Id,
                pendingDataset.ProjectId
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        await using var transaction = await _transactionFactory.BeginAsync(
            candidate.ProjectId,
            cancellationToken
        );

        if (transaction is null)
        {
            return null;
        }

        var job = await _dbContext.ImportJobs
            .SingleOrDefaultAsync(
                job =>
                    job.Id == candidate.JobId
                    && job.Status == ImportJobStatus.Pending,
                cancellationToken
            );

        if (job is null)
        {
            return null;
        }

        var version = await _dbContext.DatasetVersions
            .SingleAsync(
                version => version.Id == job.DatasetVersionId,
                cancellationToken
            );

        var dataset = await _dbContext.Datasets
            .SingleAsync(
                dataset => dataset.Id == version.DatasetId,
                cancellationToken
            );

        job.MarkRunning();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ImportJobWorkItem(
            job,
            version,
            dataset
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
