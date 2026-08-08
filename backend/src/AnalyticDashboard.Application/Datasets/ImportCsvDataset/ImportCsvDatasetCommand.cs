namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public sealed record ImportCsvDatasetCommand(
    Guid Id,
    string Name,
    string OriginalFileName,
    string StoredPath,
    int RowCount,
    int ColumnCount
);