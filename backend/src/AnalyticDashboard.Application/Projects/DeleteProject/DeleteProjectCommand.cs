namespace AnalyticDashboard.Application.Projects.DeleteProject;

public sealed record DeleteProjectCommand(
    Guid ProjectId,
    Guid OwnerId
);
