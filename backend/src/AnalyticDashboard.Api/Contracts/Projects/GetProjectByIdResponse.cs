namespace AnalyticDashboard.Api.Contracts.Projects;

public sealed record GetProjectByIdResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt
);
