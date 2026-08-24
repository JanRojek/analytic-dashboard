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
            _dbContext.Projects.Add(project);

            await _dbContext.SaveChangesAsync(
                cancellationToken
            );

            return ProjectAddOutcome.Added;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectDatabaseNames.OwnerNameUniqueIndex
        })
        {
            _dbContext.Entry(project).State = EntityState.Detached;

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

    public async Task<ProjectRenameOutcome> RenameAsync(
        Guid projectId,
        Guid ownerId,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var affectedRows = await _dbContext.Projects
                .Where(project =>
                    project.Id == projectId
                    && project.OwnerId == ownerId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.Name,
                        name
                    ),
                    cancellationToken
                );

            return affectedRows == 1
                ? ProjectRenameOutcome.Renamed
                : ProjectRenameOutcome.NotFound;
        }
        catch (PostgresException ex) when (ex is
        {
           SqlState: PostgresErrorCodes.UniqueViolation,
           ConstraintName: ProjectDatabaseNames.OwnerNameUniqueIndex
        })
        {
            return ProjectRenameOutcome.NameAlreadyExists;
        }
    }

    public async Task<ProjectDeleteOutcome> DeleteByIdAndOwnerAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Projects
            .Where(project =>
                project.Id == projectId
                && project.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows == 1
            ? ProjectDeleteOutcome.Deleted
            : ProjectDeleteOutcome.NotFound;
    }

    public async Task<int> CountByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .Where(project => project.OwnerId == ownerId)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> GetPageByOwnerAsync(
        Guid ownerId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .Where(project => project.OwnerId == ownerId)
            .AsNoTracking()
            .OrderByDescending(project => project.CreatedAtUtc)
            .ThenBy(project => project.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
