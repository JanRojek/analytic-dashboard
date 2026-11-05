
using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Widgets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Application.Widgets.UpdateDashboardWidgets;

public sealed class UpdateDashboardWidgetsHandler
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IDatasetRepository _datasetRepository;
    private readonly IWidgetRepository _widgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDashboardWidgetsHandler(
        IDashboardRepository dashboardRepository,
        IDatasetRepository datasetRepository,
        IWidgetRepository widgetRepository,
        IUnitOfWork unitOfWork)
    {
        _dashboardRepository = dashboardRepository;
        _datasetRepository = datasetRepository;
        _widgetRepository = widgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<UpdateDashboardWidgetsResult> HandleAsync(
        UpdateDashboardWidgetsCommand command,
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardRepository.GetForUpdateByIdAndProjectOwnerAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (dashboard is null)
        {
            return new UpdateDashboardWidgetsResult.DashboardNotFound();
        }

        if (command.ExpectedRevision != dashboard.Revision)
        {
            return new UpdateDashboardWidgetsResult.RevisionConflict(
                command.ExpectedRevision,
                dashboard.Revision
            );
        }

        if (!HasValidUniqueWidgetIds(command.Widgets))
        {
            return new UpdateDashboardWidgetsResult.InvalidWidgetSet(
                "Widget IDs must be non-empty and unique."
            );
        }

        var widgetCollisions = new List<WidgetCollision>();

        for (var i = 0; i < command.Widgets.Count - 1; i++)
        {
            for (var j = i + 1; j < command.Widgets.Count; j++)
            {
                var firstUpdate = command.Widgets[i];
                var secondUpdate = command.Widgets[j];

                if (Intersects(firstUpdate.Layout, secondUpdate.Layout))
                {
                    widgetCollisions.Add(
                        new WidgetCollision(firstUpdate.WidgetId, secondUpdate.WidgetId)
                    );
                }
            }
        }

        if (widgetCollisions.Count > 0)
        {
            return new UpdateDashboardWidgetsResult.LayoutCollision(widgetCollisions);
        }

        var requestedDatasetIds = command.Widgets
            .Select(widget => widget.DatasetId)
            .ToHashSet();

        var availableDatasetIds = await _datasetRepository.GetExistingDatasetIdsAsync(
            requestedDatasetIds,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        foreach (var datasetId in requestedDatasetIds)
        {
            if (!availableDatasetIds.Contains(datasetId))
            {
                return new UpdateDashboardWidgetsResult.DatasetNotFound(datasetId);
            }
        }

        var configurationErrors = new List<WidgetConfigurationError>();

        foreach (var change in command.Widgets)
        {
            try
            {
                Widget.ValidateConfiguration(
                    change.DatasetId,
                    change.Type,
                    change.Title,
                    change.GroupByColumn,
                    change.MeasureColumn,
                    change.Aggregation
                );
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidEntityNameException)
            {
                configurationErrors.Add(
                    new WidgetConfigurationError(change.WidgetId, ex.Message)
                );
            }
        }

        if (configurationErrors.Count > 0)
        {
            return new UpdateDashboardWidgetsResult.InvalidConfiguration(configurationErrors);
        }

        var existingWidgets = await _widgetRepository.GetAllForUpdateAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        var existingWidgetsById = existingWidgets
            .ToDictionary(widget => widget.Id);

        var submittedWidgetIds = command.Widgets
            .Select(change => change.WidgetId)
            .ToHashSet();

        var widgetsToCreate = command.Widgets
            .Where(change => !existingWidgetsById.ContainsKey(change.WidgetId))
            .ToList();

        var widgetsToUpdate = command.Widgets
            .Where(change => existingWidgetsById.ContainsKey(change.WidgetId))
            .ToList();

        var widgetsToDelete = existingWidgets
            .Where(widget => !submittedWidgetIds.Contains(widget.Id))
            .ToList();

        foreach (var change in widgetsToCreate)
        {
            var newWidget = new Widget(
                command.DashboardId,
                change.DatasetId,
                change.Type,
                change.Title,
                change.GroupByColumn,
                change.MeasureColumn,
                change.Aggregation,
                change.Layout,
                change.WidgetId
            );

            _widgetRepository.StageAdd(newWidget);
        }

        foreach (var change in widgetsToUpdate)
        {
            var widget = existingWidgetsById[change.WidgetId];

            widget.UpdateLayout(change.Layout);
            widget.UpdateConfiguration(
                change.DatasetId,
                change.Type,
                change.Title,
                change.GroupByColumn,
                change.MeasureColumn,
                change.Aggregation
            );
        }

        foreach (var change in widgetsToDelete)
        {
            _widgetRepository.StageRemove(change);
        }

        dashboard.AdvanceRevision();

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OptimisticConcurrencyException)
        {
            var currentRevision = await _dashboardRepository.GetDashboardCurrentRevisionAsync(
                command.DashboardId,
                command.ProjectId,
                command.OwnerId,
                cancellationToken
            );

            if (currentRevision is null)
            {
                return new UpdateDashboardWidgetsResult.DashboardNotFound();
            }

            return new UpdateDashboardWidgetsResult.RevisionConflict(
                command.ExpectedRevision,
                currentRevision.Value
            );
        }

        return new UpdateDashboardWidgetsResult.Success(dashboard.Revision);
    }

    private static bool HasValidUniqueWidgetIds(
        IReadOnlyList<DashboardWidgetUpdate> changes)
    {
        var seen = new HashSet<Guid>(changes.Count);

        return changes
            .Select(change => change.WidgetId)
            .All(widgetId => widgetId != Guid.Empty && seen.Add(widgetId));
    }

    private static bool Intersects(
        WidgetLayout first,
        WidgetLayout second)
    {
        var firstRight = (long)first.X + first.Width;
        var secondRight = (long)second.X + second.Width;

        var firstBottom = (long)first.Y + first.Height;
        var secondBottom = (long)second.Y + second.Height;

        return first.X < secondRight &&
               second.X < firstRight &&
               first.Y < secondBottom &&
               second.Y < firstBottom;
    }
}
