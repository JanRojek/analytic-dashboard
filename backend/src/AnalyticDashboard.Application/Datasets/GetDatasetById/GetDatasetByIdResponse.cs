namespace AnalyticDashboard.Application.Datasets.GetDatasetById;

public sealed record GetDatasetByIdResponse(
    Guid Id,
    string Name,
    string OriginalFileName,
    DateTime CreatedAtUtc,
    int RowCount,
    int ColumnCount
);