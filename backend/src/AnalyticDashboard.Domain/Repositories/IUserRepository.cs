using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IUserRepository
{
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken);
    
    Task AddAsync(User user, CancellationToken cancellationToken);
    
    Task SaveChangesAsync(CancellationToken cancellationToken);
    
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken);
}