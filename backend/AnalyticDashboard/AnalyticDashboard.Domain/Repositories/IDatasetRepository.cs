using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IDatasetRepository
{
    Task AddAsync(Dataset dataset, CancellationToken cancellationToken);
    
    Task<IReadOnlyList<Dataset>> GetAllAsync(CancellationToken cancellationToken);
    
    Task<Dataset?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}