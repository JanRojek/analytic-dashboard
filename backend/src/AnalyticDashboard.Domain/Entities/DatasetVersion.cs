namespace AnalyticDashboard.Domain.Entities;

public sealed class DatasetVersion
{
    public Guid Id { get; private set; }

    public Guid DatasetId { get; private set; }

    public int VersionNumber { get; private set; }

    public string OriginalFileName { get; private set; }

    public string StorageKey { get; private set; }

    public DatasetVersionStatus Status { get; private set; }

    public int? RowCount { get; private set; }

    public int? ColumnCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DatasetVersion(
        Guid datasetId,
        int versionNumber,
        string originalFileName,
        string storageKey)
    {
        if (datasetId == Guid.Empty)
        {
            throw new ArgumentException(
                "DatasetId cannot be empty."
            );
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNumber),
                "Version number must be greater than zero."
            );
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException(
                "Original file name cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException(
                "Storage key cannot be empty."
            );
        }

        Id = Guid.NewGuid();
        DatasetId = datasetId;
        VersionNumber = versionNumber;
        OriginalFileName = originalFileName;
        StorageKey = storageKey;
        Status = DatasetVersionStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void MarkReady(
        int rowCount,
        int columnCount)
    {
        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowCount)
            );
        }

        if (columnCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnCount)
            );
        }

        if (Status != DatasetVersionStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending dataset version can become ready."
            );
        }

        RowCount = rowCount;
        ColumnCount = columnCount;
        Status = DatasetVersionStatus.Ready;
    }

    public void MarkFailed()
    {
        if (Status != DatasetVersionStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only a pending dataset version can fail."
            );
        }

        Status = DatasetVersionStatus.Failed;
    }
}
