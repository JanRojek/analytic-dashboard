namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed record GetProjectsQuery(
    Guid OwnerId,
    int Page,
    int PageSize
)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}
