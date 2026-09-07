namespace AnalyticDashboard.Application.Import;

public sealed record CsvImportResult(
    string OriginalFileName,
    string StorageKey,
    int RowCount,
    int ColumnCount
);
