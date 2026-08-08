namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public sealed record GetDatasetsResponse(
    Guid Id,
    string Name,
    string OriginalFileName,
    DateTime CreatedAtUtc,
    int RowCount,
    int ColumnCount
);