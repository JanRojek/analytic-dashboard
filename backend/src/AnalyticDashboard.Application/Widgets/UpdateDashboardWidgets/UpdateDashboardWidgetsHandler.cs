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
    private readonly IProjectWriteTransactionFactory _transactionFactory;

    public UpdateDashboardWidgetsHandler(
        IDashboardRepository dashboardRepository,
        IDatasetRepository datasetRepository,
        IWidgetRepository widgetRepository,
        IProjectWriteTransactionFactory transactionFactory,
        IUnitOfWork unitOfWork)
    {
        _dashboardRepository = dashboardRepository;
        _datasetRepository = datasetRepository;
        _widgetRepository = widgetRepository;
        _transactionFactory = transactionFactory;
        _unitOfWork = unitOfWork;
    }

    public async Task<UpdateDashboardWidgetsResult> HandleAsync(
        UpdateDashboardWidgetsCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _transactionFactory.BeginAsync(
            command.ProjectId,
            cancellationToken
        );

        if (transaction is null)
        {
            return new UpdateDashboardWidgetsResult.DashboardNotFound();
        }

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

        var collision = FindFirstCollision(command.Widgets, cancellationToken);

        if (collision is not null)
        {
            return new UpdateDashboardWidgetsResult.LayoutCollision([collision]);
        }

        var existingWidgets = await _widgetRepository.GetAllForUpdateAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        var existingWidgetsById = existingWidgets
            .ToDictionary(widget => widget.Id);

        var requestedDatasetIds = command.Widgets
            .Where(widget => widget.DatasetId.HasValue)
            .Select(widget => widget.DatasetId!.Value)
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
            existingWidgetsById.TryGetValue(change.WidgetId, out var existingWidget);

            if (change.DatasetId is null
                && (existingWidget is null || existingWidget.DatasetId is not null))
            {
                configurationErrors.Add(new WidgetConfigurationError(
                    change.WidgetId,
                    "A dataset is required for new or connected widgets."
                ));
                continue;
            }

            if (existingWidget is { DatasetId: null })
            {
                if (change.DatasetId is not null)
                {
                    configurationErrors.Add(new WidgetConfigurationError(
                        change.WidgetId,
                        "Reconnecting a detached widget is not supported yet."
                    ));
                    continue;
                }

                if (change.Type != existingWidget.Type
                    || change.GroupByColumn != existingWidget.GroupByColumn
                    || change.MeasureColumn != existingWidget.MeasureColumn
                    || change.Aggregation != existingWidget.Aggregation)
                {
                    configurationErrors.Add(new WidgetConfigurationError(
                        change.WidgetId,
                        "Detached widgets must preserve their configuration."
                    ));
                    continue;
                }
            }

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
                change.DatasetId!.Value,
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

            if (change.DatasetId is { } datasetId)
            {
                widget.UpdateConfiguration(
                    datasetId,
                    change.Type,
                    change.Title,
                    change.GroupByColumn,
                    change.MeasureColumn,
                    change.Aggregation
                );
            }
            else
            {
                widget.Rename(change.Title);
            }
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

        await transaction.CommitAsync(cancellationToken);

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

    private static WidgetCollision? FindFirstCollision(
        IReadOnlyList<DashboardWidgetUpdate> widgetList,
        CancellationToken cancellationToken)
    {
        var columnOccupancy = new (Guid WidgetId, long OccupiedUntil)[WidgetLayout.Columns];

        cancellationToken.ThrowIfCancellationRequested();

        var sortedWidgets = widgetList
            .OrderBy(widget => widget.Layout.Y)
            .ToList();

        foreach (var widget in sortedWidgets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var layout = widget.Layout;
            var right = layout.X + layout.Width;

            for (var column = layout.X; column < right; column++)
            {
                if (layout.Y < columnOccupancy[column].OccupiedUntil)
                {
                    return new WidgetCollision(
                        widget.WidgetId,
                        columnOccupancy[column].WidgetId
                    );
                }
            }

            var bottom = (long)layout.Y + layout.Height;

            for (var column = layout.X; column < right; column++)
            {
                columnOccupancy[column] = (widget.WidgetId, bottom);
            }
        }

        return null;
    }
}
