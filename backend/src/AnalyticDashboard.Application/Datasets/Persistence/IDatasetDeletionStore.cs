namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IDatasetDeletionStore
{
    Task<DatasetDeletionOutcome> DeleteAsync(
        Guid datasetId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );
}

public enum DatasetDeletionOutcome
{
    Deleted,
    NotFound,
    ImportInProgress
}
