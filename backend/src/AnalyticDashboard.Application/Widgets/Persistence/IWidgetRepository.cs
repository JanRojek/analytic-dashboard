using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.Persistence;

public interface IWidgetRepository
{
    Task AddAsync(
        Widget widget,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Widget>> GetAllByDashboardProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<Widget?> GetByIdAndDashboardProjectOwnerAsync(
        Guid widgetId,
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<bool> DeleteByIdAndDashboardProjectOwnerAsync(
        Guid widgetId,
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );
}
