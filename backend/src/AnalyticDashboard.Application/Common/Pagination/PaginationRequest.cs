namespace AnalyticDashboard.Application.Common.Pagination;

public sealed record PaginationRequest(
    int Page = PaginationRequest.DefaultPage,
    int PageSize = PaginationRequest.DefaultPageSize
)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 96;
}
