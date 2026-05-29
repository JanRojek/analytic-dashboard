using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class WidgetRepository : IWidgetRepository
{
    private readonly AppDbContext _dbContext;

    public WidgetRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task AddAsync(Widget widget, CancellationToken cancellationToken)
    {
        await _dbContext.Widgets.AddAsync(widget, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Widget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Widgets
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Widget>> GetByDashboardIdAsync(
        Guid dashboardId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Widgets
            .AsNoTracking()
            .Where(w => w.DashboardId == dashboardId)
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
    
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var widget = await _dbContext.Widgets
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (widget == null)
        {
            return false;
        }
        
        _dbContext.Widgets.Remove(widget);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}