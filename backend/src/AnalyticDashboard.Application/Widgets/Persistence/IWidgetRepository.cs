using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.Persistence;

public interface IWidgetRepository
{
    void StageAdd(Widget widget);

    Task<IReadOnlyList<Widget>> GetAllByDashboardProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<Widget>> GetAllForUpdateAsync(
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

    void StageRemove(Widget widget);
}
