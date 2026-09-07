using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class DatasetRepository : IDatasetRepository
{
    private readonly AppDbContext _dbContext;

    public DatasetRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        Dataset dataset,
        CancellationToken cancellationToken)
    {
        _dbContext.Datasets.Add(dataset);

        await _dbContext.SaveChangesAsync(
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<Dataset>> GetAllByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Datasets
            .Where(dataset =>
                dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId
                )
            )
            .AsNoTracking()
            .OrderByDescending(dataset => dataset.CreatedAtUtc)
            .ThenBy(dataset => dataset.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dataset?> GetByIdAndProjectOwnerAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Datasets
            .Where(dataset =>
                dataset.Id == datasetId
                && dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId
                )
            )
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DeleteByIdAndProjectOwnerAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Datasets
            .Where(dataset =>
                dataset.Id == datasetId
                && dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId
                )
            )
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows == 1;
    }

    public async Task<bool> PublishVersionAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var dataset = await _dbContext.Datasets
            .Where(dataset =>
                dataset.Id == datasetId
                && dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId
                )
            )
            .SingleOrDefaultAsync(cancellationToken);

        if (dataset is null)
        {
            return false;
        }

        var versionBelongsToDataset =
            await _dbContext.DatasetVersions.AnyAsync(
                version =>
                    version.Id == versionId
                    && version.DatasetId == datasetId
                    && version.Status == DatasetVersionStatus.Ready,
                cancellationToken
            );

        if (!versionBelongsToDataset)
        {
            return false;
        }

        dataset.PublishVersion(
            versionId
        );

        await _dbContext.SaveChangesAsync(
            cancellationToken
        );

        return true;
    }
}
