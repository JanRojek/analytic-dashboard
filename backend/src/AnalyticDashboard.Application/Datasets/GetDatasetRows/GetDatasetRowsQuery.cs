namespace AnalyticDashboard.Application.Datasets.GetDatasetRows;

public sealed record GetDatasetRowsQuery(
    Guid DatasetId,
    Guid ProjectId,
    Guid OwnerId,
    int Page,
    int PageSize
)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}
