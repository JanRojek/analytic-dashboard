namespace AnalyticDashboard.Application.Profiling;

public sealed record DatasetProfile(
    Guid Id,
    string Name,
    string OriginalFileName,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<ColumnProfile> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> PreviewRows
);

public sealed record ColumnProfile(
    string Name,
    string Type,
    int NullCount,
    string? Min,
    string? Max,
    double? Avg
);
