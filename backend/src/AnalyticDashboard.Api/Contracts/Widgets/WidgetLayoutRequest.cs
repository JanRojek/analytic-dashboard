namespace AnalyticDashboard.Api.Contracts.Widgets;

public sealed record WidgetLayoutRequest(
    int X,
    int Y,
    int Width,
    int Height
);
