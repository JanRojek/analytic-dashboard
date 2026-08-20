namespace AnalyticDashboard.Api.Contracts.Projects;

public sealed record GetProjectsResponse(
    IReadOnlyList<GetProjectsResponseItem> Items
);

public sealed record GetProjectsResponseItem(
    Guid Id,
    string Name,
    DateTime CreatedAt
);
