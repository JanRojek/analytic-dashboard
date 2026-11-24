using AnalyticDashboard.Application.Common.Pagination;

namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public abstract record GetDashboardsResult
{
    public sealed record Success(
        PagedResult<GetDashboardsItem> Data
    ) : GetDashboardsResult;

    public sealed record NotFound : GetDashboardsResult;
}
