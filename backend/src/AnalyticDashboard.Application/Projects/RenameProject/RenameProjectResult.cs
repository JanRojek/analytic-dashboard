namespace AnalyticDashboard.Application.Projects.RenameProject;

public abstract record RenameProjectResult
{
    public sealed record Success(
        Guid Id,
        string Name,
        DateTime CreatedAtUtc
    ) : RenameProjectResult;

    public sealed record NameAlreadyExists(
        string ConflictingName
    ) : RenameProjectResult;

    public sealed record InvalidName(
        string Message
    ) : RenameProjectResult;

    public sealed record NotFound : RenameProjectResult;
}
