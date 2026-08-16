using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly AppDbContext _dbContext;

    public ProjectRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        await _dbContext.Projects.AddAsync(project, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsByOwnerAndNameAsync(
        Guid ownerId,
        string name,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .AnyAsync(
                project => project.OwnerId == ownerId
                           && project.Name == name,
                cancellationToken
            );
    }
}
