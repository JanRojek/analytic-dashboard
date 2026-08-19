namespace AnalyticDashboard.Application.Projects.GetProjectById;

public sealed record GetProjectByIdQuery(
    Guid ProjectId,
    Guid OwnerId
);
