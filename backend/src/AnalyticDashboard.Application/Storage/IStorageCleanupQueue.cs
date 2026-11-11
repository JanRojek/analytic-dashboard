namespace AnalyticDashboard.Application.Storage;

public interface IStorageCleanupQueue
{
    void EnqueueDatasetDeletion(Guid datasetId);
}
