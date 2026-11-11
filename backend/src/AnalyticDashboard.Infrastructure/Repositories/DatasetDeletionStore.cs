using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class DatasetDeletionStore : IDatasetDeletionStore
{
    private readonly AppDbContext _dbContext;
    private readonly IStorageCleanupQueue _cleanupQueue;
    private readonly IProjectWriteTransactionFactory _transactionFactory;

    public DatasetDeletionStore(
        AppDbContext dbContext,
        IStorageCleanupQueue cleanupQueue,
        IProjectWriteTransactionFactory transactionFactory)
    {
        _dbContext = dbContext;
        _cleanupQueue = cleanupQueue;
        _transactionFactory = transactionFactory;
    }

    public async Task<DatasetDeletionOutcome> DeleteAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _transactionFactory.BeginAsync(
            projectId,
            cancellationToken
        );

        if (transaction is null)
        {
            return DatasetDeletionOutcome.NotFound;
        }

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
            return DatasetDeletionOutcome.NotFound;
        }

        var hasActiveImport = await _dbContext.ImportJobs
            .AnyAsync(
                job =>
                    (
                        job.Status == ImportJobStatus.Pending
                        || job.Status == ImportJobStatus.Running
                    )
                    && _dbContext.DatasetVersions.Any(version =>
                        version.Id == job.DatasetVersionId
                        && version.DatasetId == datasetId
                    ),
                cancellationToken
            );

        if (hasActiveImport)
        {
            return DatasetDeletionOutcome.ImportInProgress;
        }

        var widgets = await _dbContext.Widgets
            .Where(widget => widget.DatasetId == datasetId)
            .OrderBy(widget => widget.Id)
            .ToListAsync(cancellationToken);

        var dashboardIds = widgets
            .Select(widget => widget.DashboardId)
            .Distinct()
            .ToArray();

        var dashboards = await _dbContext.Dashboards
            .Where(dashboard => dashboardIds.Contains(dashboard.Id))
            .OrderBy(dashboard => dashboard.Id)
            .ToListAsync(cancellationToken);

        foreach (var widget in widgets)
        {
            widget.DetachDataset();
        }

        foreach (var dashboard in dashboards)
        {
            dashboard.AdvanceRevision();
        }

        _cleanupQueue.EnqueueDatasetDeletion(datasetId);

        _dbContext.Datasets.Remove(dataset);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return DatasetDeletionOutcome.Deleted;
    }
}
