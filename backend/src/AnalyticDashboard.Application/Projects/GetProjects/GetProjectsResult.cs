namespace AnalyticDashboard.Application.Projects.GetProjects;

public abstract record GetProjectsResult
{
    public sealed record Success(
        IReadOnlyList<GetProjectsItem> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages
    ) : GetProjectsResult;

    public sealed record InvalidPageSize(
        string Message
    ) : GetProjectsResult;
}
