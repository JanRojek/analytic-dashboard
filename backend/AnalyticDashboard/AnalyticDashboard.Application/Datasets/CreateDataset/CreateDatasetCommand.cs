namespace AnalyticDashboard.Application.Datasets.CreateDataset;

public sealed record CreateDatasetCommand(
    string Name,
    string OriginalFileName,
    string StoredPath,
    int RowCount,
    int ColumnCount
);