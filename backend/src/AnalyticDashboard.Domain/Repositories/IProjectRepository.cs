using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Domain.Repositories;

public interface IProjectRepository
{
    Task<ProjectAddOutcome> AddAsync(
        Project project,
        CancellationToken cancellationToken);
}
