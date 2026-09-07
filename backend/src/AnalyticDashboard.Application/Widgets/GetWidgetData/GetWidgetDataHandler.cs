using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Application.Storage;

namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public sealed class GetWidgetDataHandler
{
    private readonly IWidgetRepository _widgetRepository;
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IWidgetDataReader _widgetDataReader;
    private readonly IFileStorage _fileStorage;

    public GetWidgetDataHandler(
        IWidgetRepository widgetRepository,
        IDashboardRepository dashboardRepository,
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IWidgetDataReader dataReader,
        IFileStorage fileStorage)
    {
        _widgetRepository = widgetRepository;
        _dashboardRepository = dashboardRepository;
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _widgetDataReader = dataReader;
        _fileStorage = fileStorage;
    }

    public async Task<GetWidgetDataResponse?> Handle(
        GetWidgetDataQuery query,
        CancellationToken cancellationToken)
    {
        var widget = await _widgetRepository.GetByIdAsync(
            query.WidgetId,
            cancellationToken
        );

        if (widget is null)
        {
            return null;
        }

        var dashboard = await _dashboardRepository.GetByIdAsync(
            widget.DashboardId,
            cancellationToken
        );

        if (dashboard is null)
        {
            return null;
        }

        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                dashboard.DatasetId,
                query.ProjectId,
                query.OwnerId,
                cancellationToken
            );

        if (dataset is not { CurrentVersionId: { } versionId })
        {
            return null;
        }

        var version = await _versionRepository.GetByIdAsync(
            versionId,
            dataset.Id,
            cancellationToken
        );

        if (version is null
            || version.Status != DatasetVersionStatus.Ready
            || version.StorageKey is null)
        {
            return null;
        }

        var fileExists = await _fileStorage.ExistsAsync(
            version.StorageKey,
            cancellationToken
        );

        if (!fileExists)
        {
            return null;
        }

        return await _widgetDataReader.ReadDataAsync(
            widget,
            version.StorageKey,
            cancellationToken
        );
    }
}
