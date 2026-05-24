using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class DashboardRepository : IDashboardRepository
{
    private readonly AppDbContext _dbContext;

    public DashboardRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken)
    {
        await _dbContext.Dashboards.AddAsync(dashboard, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Dashboards
            .AsNoTracking()
            .Include(d => d.Dataset)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Dashboards
            .AsNoTracking()
            .Include(d => d.Dataset)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }
    
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var dashboard = await _dbContext.Dashboards
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dashboard == null)
        {
            return false;
        }
        
        _dbContext.Dashboards.Remove(dashboard);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}