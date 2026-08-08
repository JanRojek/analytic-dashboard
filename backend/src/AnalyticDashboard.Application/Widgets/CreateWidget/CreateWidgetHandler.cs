using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Widgets.CreateWidget;

public sealed class CreateWidgetHandler
{
    private readonly IWidgetRepository _repository;
    private readonly IDashboardRepository _dashboardRepository;
    
    public CreateWidgetHandler(
        IWidgetRepository repository, 
        IDashboardRepository dashboardRepository)
    {
        _repository = repository;
        _dashboardRepository = dashboardRepository;
    }

    public async Task<CreateWidgetResponse> Handle(
        CreateWidgetCommand command, 
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardRepository.GetByIdAsync(
            command.DashboardId,
            cancellationToken
        );

        if (dashboard is null)
        {
            throw new InvalidOperationException(
                $"Dashboard with ID {command.DashboardId} was not found.");
        }
        
        var widget = new Widget(
            Guid.NewGuid(), 
            command.DashboardId, 
            command.Type, 
            command.Title, 
            command.XColumn, 
            command.YColumn, 
            command.Aggregation, 
            DateTime.UtcNow
        );
        
        await _repository.AddAsync(widget, cancellationToken);
        
        return new CreateWidgetResponse(widget.Id);
    }
}