using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public sealed class GetWidgetDataHandler
{
    private readonly IWidgetRepository _widgetRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IDatasetRepository _datasetRepository;
    private readonly IWidgetDataReader _widgetDataReader;

    public GetWidgetDataHandler(
        IWidgetRepository widgetRepository,
        IDashboardRepository dashboardRepository,
        IDatasetRepository datasetRepository,
        IWidgetDataReader dataReader)
    {
        _widgetRepository = widgetRepository;
        _dashboardRepository = dashboardRepository;
        _datasetRepository = datasetRepository;
        _widgetDataReader = dataReader;
    }

    public async Task<GetWidgetDataResponse?> Handle(
        GetWidgetDataQuery query, 
        CancellationToken cancellationToken)
    {
        var widget = await _widgetRepository.GetByIdAsync(query.WidgetId, cancellationToken);

        if (widget == null)
        {
            return null;
        }
        
        var dashboard = await _dashboardRepository.GetByIdAsync(widget.DashboardId, cancellationToken);

        if (dashboard == null)
        {
            return null;
        }

        var dataset = await _datasetRepository.GetByIdAsync(dashboard.DatasetId, cancellationToken);

        if (dataset == null)
        {
            return null;
        }
        
        if (!File.Exists(dataset.StoredPath))
        {
            return null;
        }

        return await _widgetDataReader.ReadDataAsync(
            widget,
            dataset.StoredPath,
            cancellationToken
        );
    }
}