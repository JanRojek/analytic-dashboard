using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class PendingDatasetImportRepository : IPendingDatasetImportRepository
{
    private readonly AppDbContext _dbContext;

    public PendingDatasetImportRepository(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(
        Dataset dataset,
        DatasetVersion version,
        ImportJob importJob,
        CancellationToken cancellationToken)
    {
        _dbContext.Datasets.Add(dataset);
        _dbContext.DatasetVersions.Add(version);
        _dbContext.ImportJobs.Add(importJob);

        await _dbContext.SaveChangesAsync(
            cancellationToken
        );
    }
}
