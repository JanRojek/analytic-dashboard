namespace AnalyticDashboard.Application.Projects.CreateProject;

public abstract record CreateProjectResult
{
    public sealed record Success(
        Guid Id,
        string Name,
        DateTime CreatedAt
    ) : CreateProjectResult;

    public sealed record NameAlreadyExists(
        string RequestedName
    ) : CreateProjectResult;
}
