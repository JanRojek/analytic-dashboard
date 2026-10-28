namespace AnalyticDashboard.Application.Dashboards.UpdateDashboardLayout;

public abstract record UpdateDashboardLayoutResult
{
    public sealed record Success : UpdateDashboardLayoutResult;

    public sealed record DashboardNotFound : UpdateDashboardLayoutResult;

    public sealed record InvalidWidgetSet(
        string Message
    ) : UpdateDashboardLayoutResult;

    public sealed record LayoutCollision : UpdateDashboardLayoutResult;
}
