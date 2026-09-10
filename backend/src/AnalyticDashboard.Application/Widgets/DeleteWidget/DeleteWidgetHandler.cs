using AnalyticDashboard.Application.Widgets.Persistence;

namespace AnalyticDashboard.Application.Widgets.DeleteWidget;

public sealed class DeleteWidgetHandler
{
    private readonly IWidgetRepository _widgetRepository;

    public DeleteWidgetHandler(IWidgetRepository widgetRepository)
    {
        _widgetRepository = widgetRepository;
    }

    public async Task<bool> HandleAsync(
        DeleteWidgetCommand command,
        CancellationToken cancellationToken)
    {
        return await _widgetRepository.DeleteByIdAndDashboardProjectOwnerAsync(
            command.Id,
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );
    }
}
