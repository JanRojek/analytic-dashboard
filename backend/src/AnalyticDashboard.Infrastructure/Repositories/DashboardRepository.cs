using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
               {
                   SqlState: PostgresErrorCodes.ForeignKeyViolation,
                   TableName: "Dashboards",
                   ConstraintName: "FK_Dashboards_projects_ProjectId"
               })
        {
            throw new MissingReferenceException(nameof(Project), exception);
        }
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

    public async Task<Dashboard?> GetForUpdateByIdAndProjectOwnerAsync(
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
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<long?> GetDashboardCurrentRevisionAsync(
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
            .Select(dashboard => (long?)dashboard.Revision)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
