using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Dashboards.DeleteDashboard;

public sealed class DeleteDashboardHandler
{
    private readonly IDashboardRepository _repository;
    
    public DeleteDashboardHandler(IDashboardRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(
        DeleteDashboardCommand command,
        CancellationToken cancellationToken)
    {
        return await _repository.DeleteAsync(command.Id, cancellationToken);
    }
}