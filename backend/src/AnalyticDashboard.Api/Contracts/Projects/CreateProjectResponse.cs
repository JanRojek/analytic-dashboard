namespace AnalyticDashboard.Api.Contracts.Projects;

public sealed record CreateProjectResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc
);
