using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Dashboards.Persistence;

public sealed record DashboardEditorSnapshot(
    Dashboard Dashboard,
    IReadOnlyList<Widget> Widgets
);
