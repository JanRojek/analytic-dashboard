namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record DatasetResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    DatasetCurrentVersionResponse? CurrentVersion
);

public sealed record DatasetCurrentVersionResponse(
    Guid Id,
    int VersionNumber,
    string OriginalFileName,
    int RowCount,
    int ColumnCount
);
