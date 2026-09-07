namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record DatasetProfileResponse(
    Guid Id,
    string Name,
    string OriginalFileName,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<DatasetColumnProfileResponse> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> PreviewRows
);

public sealed record DatasetColumnProfileResponse(
    string Name,
    string Type,
    int NullCount,
    string? Min,
    string? Max,
    double? Avg
);
