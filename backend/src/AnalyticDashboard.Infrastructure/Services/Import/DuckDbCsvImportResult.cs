namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed record DuckDbCsvImportResult(
    string StorageKey,
    int RowCount,
    int ColumnCount
);
