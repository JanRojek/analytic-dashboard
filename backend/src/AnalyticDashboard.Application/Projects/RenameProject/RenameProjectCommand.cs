namespace AnalyticDashboard.Application.Projects.RenameProject;

public sealed record RenameProjectCommand(
    Guid ProjectId,
    Guid OwnerId,
    string Name
);
