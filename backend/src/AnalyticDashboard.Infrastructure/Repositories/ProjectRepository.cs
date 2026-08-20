using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly AppDbContext _dbContext;

    public ProjectRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProjectAddOutcome> AddAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.Projects.AddAsync(
                project,
                cancellationToken
            );

            await _dbContext.SaveChangesAsync(
                cancellationToken
            );

            return ProjectAddOutcome.Added;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
           SqlState: PostgresErrorCodes.UniqueViolation,
           ConstraintName: "IX_projects_OwnerId_Name"
        })
        {
            return ProjectAddOutcome.NameAlreadyExists;
        }
    }

    public async Task<Project?> GetByIdAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                project => project.Id == projectId
                           && project.OwnerId == ownerId,
                cancellationToken
            );
    }

    public async Task<IReadOnlyList<Project>> GetByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .Where(project => project.OwnerId == ownerId)
            .AsNoTracking()
            .OrderByDescending(project => project.CreatedAt)
            .ThenBy(project => project.Id)
            .ToListAsync(cancellationToken);
    }
}
