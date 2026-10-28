namespace AnalyticDashboard.Application.Widgets.UpdateDashboardWidgets;

public abstract record UpdateDashboardWidgetsResult
{
    public sealed record Success : UpdateDashboardWidgetsResult;

    public sealed record DashboardNotFound : UpdateDashboardWidgetsResult;

    public sealed record InvalidWidgetSet(string Message) : UpdateDashboardWidgetsResult;

    public sealed record InvalidConfiguration(IReadOnlyList<WidgetConfigurationError> Errors)
        : UpdateDashboardWidgetsResult;

    public sealed record DatasetNotFound(Guid DatasetId) : UpdateDashboardWidgetsResult;

    public sealed record LayoutCollision(IReadOnlyList<WidgetCollision> Collisions) : UpdateDashboardWidgetsResult;
}

public sealed record WidgetCollision(Guid FirstWidgetId, Guid SecondWidgetId);
public sealed record WidgetConfigurationError(Guid WidgetId, string Reason);
