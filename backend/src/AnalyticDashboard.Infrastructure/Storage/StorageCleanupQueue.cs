using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Data;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageCleanupQueue : IStorageCleanupQueue
{
    private readonly AppDbContext _dbContext;

    public StorageCleanupQueue(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void EnqueueDatasetDeletion(Guid datasetId)
    {
        var job = new StorageCleanupJob(datasetId);

        _dbContext.StorageCleanupJobs.Add(job);
    }
}
