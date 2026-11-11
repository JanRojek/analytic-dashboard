namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageCleanupJob
{
    public Guid Id { get; private set; }

    public Guid DatasetId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime NextAttemptAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public StorageCleanupJob(Guid datasetId)
    {
        if (datasetId == Guid.Empty)
        {
            throw new ArgumentException(
                "DatasetId cannot be empty.",
                nameof(datasetId)
            );
        }

        Id = Guid.NewGuid();
        DatasetId = datasetId;
        CreatedAtUtc = DateTime.UtcNow;
        NextAttemptAtUtc = CreatedAtUtc;
    }

    private StorageCleanupJob() {}

    public void ScheduleRetry(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        AttemptCount++;
        NextAttemptAtUtc = DateTime.UtcNow.Add(delay);
    }
}
