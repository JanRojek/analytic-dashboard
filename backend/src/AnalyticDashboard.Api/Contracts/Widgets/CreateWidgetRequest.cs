using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Widgets;

public sealed record CreateWidgetRequest(
    WidgetType Type,
    string Title,
    string? XColumn,
    string? YColumn,
    string Aggregation
);