namespace AnalyticDashboard.Application.Projects.GetProjectById;

public abstract record GetProjectByIdResult
{
    public sealed record Found(
        Guid Id,
        string Name,
        DateTime CreatedAt
    ) : GetProjectByIdResult;

    public sealed record NotFound : GetProjectByIdResult;
}
