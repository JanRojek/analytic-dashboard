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

    private IQueryable<Dataset> BuildListQuery(
        Guid projectId,
        Guid ownerId,
        string? search,
        bool readyOnly)
    {
        var query = _dbContext.Datasets
            .AsNoTracking()
            .Where(dataset =>
                dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId
                )
            );

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            var pattern = $"%{term}%";

            query = query.Where(dataset =>
                EF.Functions.ILike(dataset.Name, pattern, "\\")
            );
        }

        if (readyOnly)
        {
            query = query.Where(dataset =>
                dataset.CurrentVersionId.HasValue
                && _dbContext.DatasetVersions.Any(version =>
                    version.Id == dataset.CurrentVersionId.Value
                    && version.DatasetId == dataset.Id
                    && version.Status == DatasetVersionStatus.Ready
                    && version.RowCount != null
                    && version.ColumnCount != null
                )
            );
        }

        return query;
    }

    public async Task<int> CountByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        string? search,
        bool readyOnly,
        CancellationToken cancellationToken)
    {
        return await BuildListQuery(
            projectId,
            ownerId,
            search,
            readyOnly
        ).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Dataset>> GetPageByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        string? search,
        bool readyOnly,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await BuildListQuery(
            projectId,
            ownerId,
            search,
            readyOnly
        )
        .OrderByDescending(dataset => dataset.CreatedAtUtc)
        .ThenBy(dataset => dataset.Id)
        .Skip(skip)
        .Take(take)
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

    public async Task<HashSet<Guid>> GetExistingDatasetIdsAsync(
        IReadOnlyCollection<Guid> datasetIds,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var ids = await _dbContext.Datasets
            .Where(dataset =>
                datasetIds.Contains(dataset.Id)
                && dataset.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dataset.ProjectId
                    && project.OwnerId == ownerId))
            .Select(dataset => dataset.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<DatasetSummary> GetSummaryByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var summary = await (
            from dataset in BuildListQuery(
                projectId,
                ownerId,
                search: null,
                readyOnly: false
            )
            join version in _dbContext.DatasetVersions.AsNoTracking()
                on new
                {
                    VersionId = dataset.CurrentVersionId,
                    DatasetId = dataset.Id
                }
                equals new
                {
                    VersionId = (Guid?)version.Id,
                    version.DatasetId
                }
                into versions
            from version in versions.DefaultIfEmpty()
            group version by 1 into grouped
            select new DatasetSummary(
                grouped.Count(),

                grouped.Count(version =>
                    version != null
                    && version.Status == DatasetVersionStatus.Ready
                    && version.RowCount != null
                    && version.ColumnCount != null
                ),

                grouped.Sum(version =>
                    version != null
                    && version.Status == DatasetVersionStatus.Ready
                    && version.RowCount != null
                    && version.ColumnCount != null
                        ? version.RowCount
                        : 0L
                ) ?? 0L
            )
        ).SingleOrDefaultAsync(cancellationToken);

        return summary ?? new DatasetSummary(0, 0, 0);
    }
}
