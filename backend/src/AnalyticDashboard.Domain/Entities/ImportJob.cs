namespace AnalyticDashboard.Domain.Entities;

public sealed class ImportJob
{
    public Guid Id { get; private set; }

    public Guid DatasetVersionId { get; private set; }

    public string SourceFileName { get; private set; }

    public string SourceStorageKey { get; private set; }

    public ImportJobStatus Status { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public ImportJob(
        Guid datasetVersionId,
        string sourceFileName,
        string sourceStorageKey)
    {
        if (datasetVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "DatasetVersionId cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            throw new ArgumentException(
                "Source file name cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(sourceStorageKey))
        {
            throw new ArgumentException(
                "Source storage key cannot be empty."
            );
        }

        Id = Guid.NewGuid();
        DatasetVersionId = datasetVersionId;
        SourceFileName = sourceFileName;
        SourceStorageKey = sourceStorageKey;
        Status = ImportJobStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void MarkRunning()
    {
        if (Status != ImportJobStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending import job can start."
            );
        }

        Status = ImportJobStatus.Running;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        if (Status != ImportJobStatus.Running)
        {
            throw new InvalidOperationException(
                "Only a running import job can complete."
            );
        }

        Status = ImportJobStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(
        string errorMessage)
    {
        if (Status is not (
            ImportJobStatus.Pending
            or ImportJobStatus.Running))
        {
            throw new InvalidOperationException(
                "Only a pending or running import job can fail."
            );
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException(
                "Error message cannot be empty."
            );
        }

        Status = ImportJobStatus.Failed;
        ErrorMessage = errorMessage.Trim();
        CompletedAtUtc = DateTime.UtcNow;
    }
}
