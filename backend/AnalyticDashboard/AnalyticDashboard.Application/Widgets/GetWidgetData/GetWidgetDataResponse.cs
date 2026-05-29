namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public sealed record GetWidgetDataResponse(
    string Type,
    string Title,
    string? Value,
    IReadOnlyList<string> Labels,
    IReadOnlyList<double> Values
);