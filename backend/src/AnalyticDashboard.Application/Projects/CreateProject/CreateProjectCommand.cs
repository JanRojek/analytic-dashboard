namespace AnalyticDashboard.Application.Projects.CreateProject;

public sealed record CreateProjectCommand(
    Guid OwnerId,
    string Name
);
