using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Domain.Entities;

public sealed class Widget
{
    public Guid Id { get; private set; }

    public Guid DashboardId { get; private set; }

    public Guid DatasetId { get; private set; }

    public WidgetType Type { get; private set; }

    public string Title { get; private set; }

    public string GroupByColumn { get; private set; }

    public string MeasureColumn { get; private set; }

    public AggregationType Aggregation { get; private set; }

    public WidgetLayout Layout { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public const int MaxTitleLength = 200;

    public const int MaxGroupByColumnLength = 255;

    public const int MaxMeasureColumnLength = 255;

    public Widget(
        Guid dashboardId,
        Guid datasetId,
        WidgetType type,
        string title,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation,
        WidgetLayout layout)
    {
        if (dashboardId == Guid.Empty)
        {
            throw new ArgumentException("DashboardId cannot be empty.");
        }

        ValidateConfiguration(datasetId, type, title, groupByColumn, measureColumn, aggregation);

        ArgumentNullException.ThrowIfNull(layout);

        Id = Guid.NewGuid();
        DashboardId = dashboardId;
        DatasetId = datasetId;
        Type = type;
        Title = NormalizeTitle(title);
        GroupByColumn = NormalizeColumnName(groupByColumn, nameof(GroupByColumn), MaxGroupByColumnLength);
        MeasureColumn = NormalizeColumnName(measureColumn, nameof(MeasureColumn), MaxMeasureColumnLength);
        Aggregation = aggregation;
        Layout = layout;
        CreatedAtUtc = DateTime.UtcNow;
    }

    private Widget()
    {
        Title = null!;

        GroupByColumn = null!;

        MeasureColumn = null!;

        Layout = null!;
    }

    public void Rename(string title)
    {
        Title = NormalizeTitle(title);
    }

    public static void ValidateConfiguration(
        Guid datasetId,
        WidgetType type,
        string title,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation)
    {
        if (datasetId == Guid.Empty)
        {
            throw new ArgumentException("DatasetId cannot be empty.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported widget type."
            );
        }

        if (!Enum.IsDefined(aggregation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aggregation),
                aggregation,
                "Unsupported aggregation type."
            );
        }

        NormalizeTitle(title);
        NormalizeColumnName(groupByColumn, nameof(GroupByColumn), MaxGroupByColumnLength);
        NormalizeColumnName(measureColumn, nameof(MeasureColumn), MaxMeasureColumnLength);
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidEntityNameException(
                nameof(Widget),
                "Widget title cannot be empty.");
        }

        title = title.Trim();

        if (title.EnumerateRunes().Count() > MaxTitleLength)
        {
            throw new InvalidEntityNameException(
                nameof(Widget),
                $"Widget title cannot be longer than {MaxTitleLength} characters.");
        }

        return title;
    }

    private static string NormalizeColumnName(
        string columnName,
        string propertyName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException(
                $"{propertyName} cannot be empty."
            );
        }

        columnName = columnName.Trim();

        if (columnName.EnumerateRunes().Count() > maxLength)
        {
            throw new ArgumentException(
                $"{propertyName} cannot be longer than {maxLength} characters."
            );
        }

        return columnName;
    }

    public void UpdateLayout(WidgetLayout newLayout)
    {
        ArgumentNullException.ThrowIfNull(newLayout);

        Layout = newLayout;
    }

    public void UpdateConfiguration(
        Guid datasetId,
        WidgetType type,
        string title,
        string groupByColumn,
        string measureColumn,
        AggregationType aggregation)
    {
        ValidateConfiguration(datasetId, type, title, groupByColumn, measureColumn, aggregation);

        DatasetId = datasetId;
        Type = type;
        Title = NormalizeTitle(title);
        GroupByColumn = NormalizeColumnName(groupByColumn, nameof(GroupByColumn), MaxGroupByColumnLength);
        MeasureColumn = NormalizeColumnName(measureColumn, nameof(MeasureColumn), MaxMeasureColumnLength);
        Aggregation = aggregation;
    }
}
