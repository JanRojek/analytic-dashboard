namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed record GetProjectsItem(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc
);
