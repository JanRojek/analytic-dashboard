namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public abstract record CreateDashboardResult
{
    public sealed record Success(
        Guid Id
    ) : CreateDashboardResult;

    public sealed record ProjectNotFound
        : CreateDashboardResult;

    public sealed record InvalidName(
        string Message
    ) : CreateDashboardResult;
}
