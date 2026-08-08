namespace AnalyticDashboard.Domain.Entities;

public sealed class Widget
{
    public Guid Id { get; private set; }
    public Guid DashboardId { get; private set; }
    public Dashboard? Dashboard { get; private set; }

    public WidgetType Type { get; private set; }
    public string Title { get; private set; }

    public string? XColumn { get; private set; }
    public string? YColumn { get; private set; }
    public string Aggregation { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Widget(
        Guid id,
        Guid dashboardId,
        WidgetType type,
        string title,
        string? xColumn,
        string? yColumn,
        string aggregation,
        DateTime createdAtUtc)
    {
        Id = id;
        DashboardId = dashboardId;
        Type = type;
        Title = title;
        XColumn = xColumn;
        YColumn = yColumn;
        Aggregation = aggregation;
        CreatedAtUtc = createdAtUtc;
    }
}