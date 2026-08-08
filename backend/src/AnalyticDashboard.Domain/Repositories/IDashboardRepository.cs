using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IDashboardRepository
{
    Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken);
    
    Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken);
    
    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}