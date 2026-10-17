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

    public async Task<ImportJobWorkItem?> GetNextPendingAsync(
        CancellationToken cancellationToken)
    {
        var job = await _dbContext.ImportJobs
            .Where(job =>
                job.Status == ImportJobStatus.Pending
            )
            .OrderBy(job => job.CreatedAtUtc)
            .ThenBy(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        var version = await _dbContext.DatasetVersions
            .SingleAsync(
                version =>
                    version.Id == job.DatasetVersionId,
                cancellationToken
            );

        var dataset = await _dbContext.Datasets
            .SingleAsync(
                dataset =>
                    dataset.Id == version.DatasetId,
                cancellationToken
            );

        return new ImportJobWorkItem(
            job,
            version,
            dataset
        );
    }

    public async Task<ImportJob?> GetByIdAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ImportJobs
            .SingleOrDefaultAsync(
                job => job.Id == importJobId,
                cancellationToken
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
