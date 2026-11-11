using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ProjectDeletionStore : IProjectDeletionStore
{
    private readonly AppDbContext _dbContext;
    private readonly IStorageCleanupQueue _cleanupQueue;

    public ProjectDeletionStore(
        AppDbContext dbContext,
        IStorageCleanupQueue cleanupQueue)
    {
        _dbContext = dbContext;
        _cleanupQueue = cleanupQueue;
    }

    public async Task<ProjectDeletionOutcome> DeleteAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var lockedProjectIds = await _dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT p."Id" AS "Value"
                FROM projects AS p
                WHERE p."Id" = {projectId}
                  AND p."OwnerId" = {ownerId}
                FOR UPDATE OF p
                """
            )
            .ToListAsync(cancellationToken);

        if (lockedProjectIds.Count == 0)
        {
            return ProjectDeletionOutcome.NotFound;
        }

        var hasActiveImport = await (
            from dataset in _dbContext.Datasets
            join version in _dbContext.DatasetVersions
                on dataset.Id equals version.DatasetId
            join job in _dbContext.ImportJobs
                on version.Id equals job.DatasetVersionId
            where dataset.ProjectId == projectId
                && (
                    job.Status == ImportJobStatus.Pending
                    || job.Status == ImportJobStatus.Running
                )
            select job.Id
        ).AnyAsync(cancellationToken);

        if (hasActiveImport)
        {
            return ProjectDeletionOutcome.ImportInProgress;
        }

        var datasetIds = await _dbContext.Datasets
            .Where(dataset => dataset.ProjectId == projectId)
            .Select(dataset => dataset.Id)
            .ToListAsync(cancellationToken);

        foreach (var datasetId in datasetIds)
        {
            _cleanupQueue.EnqueueDatasetDeletion(datasetId);
        }

        if (datasetIds.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var affectedRows = await _dbContext.Projects
            .Where(project =>
                project.Id == projectId
                && project.OwnerId == ownerId
            )
            .ExecuteDeleteAsync(cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "Project deletion affected an unexpected number of rows."
            );
        }

        await transaction.CommitAsync(cancellationToken);

        return ProjectDeletionOutcome.Deleted;
    }
}
