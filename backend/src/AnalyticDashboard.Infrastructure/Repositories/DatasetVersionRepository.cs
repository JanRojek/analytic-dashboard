using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class DatasetVersionRepository
    : IDatasetVersionRepository
{
    private readonly AppDbContext _dbContext;

    public DatasetVersionRepository(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        DatasetVersion version,
        CancellationToken cancellationToken)
    {
        _dbContext.DatasetVersions.Add(version);

        await _dbContext.SaveChangesAsync(
            cancellationToken
        );
    }

    public async Task<DatasetVersion?> GetByIdAsync(
        Guid versionId,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.DatasetVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                version =>
                    version.Id == versionId
                    && version.DatasetId == datasetId,
                cancellationToken
            );
    }

    public async Task<IReadOnlyList<DatasetVersion>> GetByIdsAsync(
        IReadOnlyCollection<Guid> versionIds,
        CancellationToken cancellationToken)
    {
        if (versionIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.DatasetVersions
            .Where(version =>
                versionIds.Contains(version.Id)
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DatasetVersion>> GetAllByDatasetIdAsync(
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.DatasetVersions
            .Where(version => version.DatasetId == datasetId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
