namespace AnalyticDashboard.Application.Import;

public sealed record CsvImportResult(
    Guid Id,
    string OriginalFileName,
    string StoredPath,
    int RowCount,
    int ColumnCount
);