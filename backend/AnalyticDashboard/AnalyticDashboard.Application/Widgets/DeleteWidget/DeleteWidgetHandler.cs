using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Widgets.DeleteWidget;

public sealed class DeleteWidgetHandler
{
    private readonly IWidgetRepository _repository;
    
    public DeleteWidgetHandler(IWidgetRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(
        DeleteWidgetCommand command,
        CancellationToken cancellationToken)
    {
        return await _repository.DeleteAsync(command.Id, cancellationToken);
    }
}