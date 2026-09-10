using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Domain.Entities;
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

    public async Task AddAsync(
        Dashboard dashboard,
        CancellationToken cancellationToken)
    {
        await _dbContext.Dashboards.AddAsync(
            dashboard,
            cancellationToken
        );

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Dashboard>> GetAllByProjectAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Dashboards
            .Where(dashboard =>
                dashboard.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dashboard.ProjectId
                    && project.OwnerId == ownerId))
            .AsNoTracking()
            .OrderByDescending(dashboard => dashboard.CreatedAtUtc)
            .ThenBy(dashboard => dashboard.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dashboard?> GetByIdAndProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Dashboards
            .Where(dashboard =>
                dashboard.Id == dashboardId
                && dashboard.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dashboard.ProjectId
                    && project.OwnerId == ownerId))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DeleteByIdAndProjectOwnerAsync(
        Guid dashboardId,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Dashboards
            .Where(dashboard =>
                dashboard.Id == dashboardId
                && dashboard.ProjectId == projectId
                && _dbContext.Projects.Any(project =>
                    project.Id == dashboard.ProjectId
                    && project.OwnerId == ownerId))
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows == 1;
    }
}
