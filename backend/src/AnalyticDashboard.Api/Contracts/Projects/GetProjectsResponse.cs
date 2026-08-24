namespace AnalyticDashboard.Api.Contracts.Projects;

public sealed record GetProjectsResponse(
    IReadOnlyList<GetProjectsResponseItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

public sealed record GetProjectsResponseItem(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc
);
