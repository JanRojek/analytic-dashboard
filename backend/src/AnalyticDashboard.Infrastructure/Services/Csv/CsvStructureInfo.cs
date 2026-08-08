namespace AnalyticDashboard.Infrastructure.Services.Csv;

public sealed record CsvStructureInfo(
    int ColumnCount,
    int RowCount
);