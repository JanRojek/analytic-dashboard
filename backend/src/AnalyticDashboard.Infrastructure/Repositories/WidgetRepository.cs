using AnalyticDashboard.Application.Widgets.Persistence;
using AnalyticDashboard.Domain.Entities;
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

    public void StageAdd(Widget widget)
    {
        _dbContext.Widgets.Add(widget);
    }

    public async Task<IReadOnlyList<Widget>> GetAllByDashboardProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Widgets
            .Where(widget =>
                widget.DashboardId == dashboardId
                && _dbContext.Dashboards.Any(dashboard =>
                    dashboard.Id == widget.DashboardId
                    && dashboard.ProjectId == projectId
                    && _dbContext.Projects.Any(project =>
                        project.Id == dashboard.ProjectId
                        && project.OwnerId == ownerId)))
            .AsNoTracking()
            .OrderBy(widget => widget.CreatedAtUtc)
            .ThenBy(widget => widget.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Widget>> GetAllForUpdateAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Widgets
            .Where(widget =>
                widget.DashboardId == dashboardId
                && _dbContext.Dashboards.Any(dashboard =>
                    dashboard.Id == widget.DashboardId
                    && dashboard.ProjectId == projectId
                    && _dbContext.Projects.Any(project =>
                        project.Id == dashboard.ProjectId
                        && project.OwnerId == ownerId)))
            .OrderBy(widget => widget.CreatedAtUtc)
            .ThenBy(widget => widget.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Widget?> GetByIdAndDashboardProjectOwnerAsync(
        Guid widgetId,
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Widgets
            .Where(widget =>
                widget.Id == widgetId
                && widget.DashboardId == dashboardId
                && _dbContext.Dashboards.Any(dashboard =>
                    dashboard.Id == widget.DashboardId
                    && dashboard.ProjectId == projectId
                    && _dbContext.Projects.Any(project =>
                        project.Id == dashboard.ProjectId
                        && project.OwnerId == ownerId)))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void StageRemove(Widget widget)
    {
        _dbContext.Widgets.Remove(widget);
    }
}
