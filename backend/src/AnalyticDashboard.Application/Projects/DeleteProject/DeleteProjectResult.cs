namespace AnalyticDashboard.Application.Projects.DeleteProject;

public abstract record DeleteProjectResult
{
    public sealed record Success : DeleteProjectResult;

    public sealed record NotFound : DeleteProjectResult;
}
