namespace AnalyticDashboard.Domain.Entities;

public sealed class Dataset
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string OriginalFileName { get; private set; }
    public string StoredPath { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }

    public Dataset(
        Guid id,
        string name,
        string originalFileName,
        string storedPath,
        DateTime createdAtUtc,
        int rowCount,
        int columnCount)
    {
        Id = id;
        Name = name;
        OriginalFileName = originalFileName;
        StoredPath = storedPath;
        CreatedAtUtc = createdAtUtc;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }
}