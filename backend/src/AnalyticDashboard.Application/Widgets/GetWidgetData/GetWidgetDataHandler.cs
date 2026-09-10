using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Application.Widgets.Persistence;

namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public sealed class GetWidgetDataHandler
{
    private readonly IWidgetRepository _widgetRepository;
    private readonly DatasetQueryExecutor _queryExecutor;

    public GetWidgetDataHandler(
        IWidgetRepository widgetRepository,
        DatasetQueryExecutor queryExecutor)
    {
        _widgetRepository = widgetRepository;
        _queryExecutor = queryExecutor;
    }

    public async Task<GetWidgetDataResult> HandleAsync(
        GetWidgetDataQuery query,
        CancellationToken cancellationToken)
    {
        var widget =
            await _widgetRepository
                .GetByIdAndDashboardProjectOwnerAsync(
                    query.WidgetId,
                    query.DashboardId,
                    query.ProjectId,
                    query.OwnerId,
                    cancellationToken
                );

        if (widget is null)
        {
            return new GetWidgetDataResult.NotFound();
        }

        var queryResult = await _queryExecutor.ExecuteAsync(
            widget.DatasetId,
            query.ProjectId,
            query.OwnerId,
            widget.GroupByColumn,
            widget.MeasureColumn,
            widget.Aggregation,
            cancellationToken
        );

        return queryResult switch
        {
            DatasetQueryExecutionResult.Success success =>
                new GetWidgetDataResult.Success(
                    widget.Id,
                    widget.Type,
                    widget.Title,
                    success.Items
                        .Select(item =>
                            new GetWidgetDataResult.Item(
                                item.Label,
                                item.Value
                            )
                        )
                        .ToList()
                ),

            DatasetQueryExecutionResult.NotFound =>
                new GetWidgetDataResult.NotFound(),

            DatasetQueryExecutionResult.InvalidQuery invalid =>
                new GetWidgetDataResult.InvalidQuery(
                    invalid.Message
                ),

            _ => throw new InvalidOperationException(
                "Unsupported dataset query execution result."
            )
        };
    }
}
