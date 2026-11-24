namespace AnalyticDashboard.Api.Contracts.Dashboards;

public sealed record GetDashboardsResponse(
    IReadOnlyList<GetDashboardsResponseItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

public sealed record GetDashboardsResponseItem(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTime CreatedAtUtc
);
