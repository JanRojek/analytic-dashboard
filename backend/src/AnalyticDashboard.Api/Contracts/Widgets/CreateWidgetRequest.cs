using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Widgets;

public sealed record CreateWidgetRequest(
    Guid DatasetId,
    WidgetType Type,
    string Title,
    string GroupByColumn,
    string MeasureColumn,
    string Aggregation
);
