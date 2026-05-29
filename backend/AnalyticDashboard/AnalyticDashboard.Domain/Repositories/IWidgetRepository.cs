using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IWidgetRepository
{
    Task AddAsync(Widget widget, CancellationToken cancellationToken);
    
    Task<Widget?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Widget>> GetByDashboardIdAsync(Guid dashboardId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}