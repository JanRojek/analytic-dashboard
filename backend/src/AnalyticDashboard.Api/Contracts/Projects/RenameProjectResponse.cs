namespace AnalyticDashboard.Api.Contracts.Projects;

public sealed record RenameProjectResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc
);
