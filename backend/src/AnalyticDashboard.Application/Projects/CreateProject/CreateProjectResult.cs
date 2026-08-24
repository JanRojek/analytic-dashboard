namespace AnalyticDashboard.Application.Projects.CreateProject;

public abstract record CreateProjectResult
{
    public sealed record Success(
        Guid Id,
        string Name,
        DateTime CreatedAtUtc
    ) : CreateProjectResult;

    public sealed record NameAlreadyExists(
        string ConflictingName
    ) : CreateProjectResult;

    public sealed record InvalidName(
        string Message
    ) : CreateProjectResult;
}
