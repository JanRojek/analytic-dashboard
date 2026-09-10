using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Widgets.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.CreateWidget;

public sealed class CreateWidgetHandler
{
    private readonly IWidgetRepository _widgetRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IDatasetRepository _datasetRepository;

    public CreateWidgetHandler(
        IWidgetRepository widgetRepository,
        IDashboardRepository dashboardRepository,
        IDatasetRepository datasetRepository)
    {
        _widgetRepository = widgetRepository;
        _dashboardRepository = dashboardRepository;
        _datasetRepository = datasetRepository;
    }

    public async Task<CreateWidgetResponse?> HandleAsync(
        CreateWidgetCommand command,
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardRepository.GetByIdAndProjectOwnerAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (dashboard is null)
        {
            return null;
        }

        var dataset = await _datasetRepository.GetByIdAndProjectOwnerAsync(
            command.DatasetId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (dataset is null)
        {
            return null;
        }

        var widget = new Widget(
            command.DashboardId,
            command.DatasetId,
            command.Type,
            command.Title,
            command.GroupByColumn,
            command.MeasureColumn,
            command.Aggregation
        );

        await _widgetRepository.AddAsync(
            widget,
            cancellationToken
        );

        return new CreateWidgetResponse(
            widget.Id
        );
    }
}
