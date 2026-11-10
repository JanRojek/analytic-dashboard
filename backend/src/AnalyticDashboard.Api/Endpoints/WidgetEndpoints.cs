using System.Diagnostics;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Widgets.GetWidgetData;
using AnalyticDashboard.Application.Widgets.GetWidgets;
using AnalyticDashboard.Application.Widgets.UpdateDashboardWidgets;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Api.Endpoints;

public static class WidgetEndpoints
{
    public static IEndpointRouteBuilder MapWidgetsEndpoints(
        this IEndpointRouteBuilder app)
    {
        var widgets = app
            .MapGroup("/projects/{projectId:guid}/dashboards/{dashboardId:guid}/widgets")
            .WithTags("Widgets")
            .RequireAuthorization()
            .ProducesProblem(
                StatusCodes.Status401Unauthorized
            )
            .ProducesProblem(
                StatusCodes.Status500InternalServerError
            );

        widgets.MapGet("", async (
            Guid projectId,
            Guid dashboardId,
            GetWidgetsHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The authenticated user identifier is missing or invalid."
                );
            }

            var query = new GetWidgetsQuery(
                dashboardId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return Results.Ok(result);
        })
        .WithName("GetWidgets")
        .Produces<IReadOnlyList<GetWidgetsResponse>>();

        widgets.MapGet("/{widgetId:guid}/data", async (
            Guid projectId,
            Guid dashboardId,
            Guid widgetId,
            GetWidgetDataHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The authenticated user identifier is missing or invalid."
                );
            }

            var query = new GetWidgetDataQuery(
                widgetId,
                dashboardId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetWidgetDataResult.Success success =>
                    Results.Ok(success),

                GetWidgetDataResult.NotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Widget data not found",
                        detail: "The widget or its dataset does not exist or is not accessible by the current user."
                    ),

                GetWidgetDataResult.SourceRemoved =>
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Widget source removed",
                        detail: "Reconnect a dataset to view this widget's data.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["code"] = "widget_source_removed"
                        }
                    ),

                GetWidgetDataResult.InvalidQuery invalid =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget query",
                        detail: invalid.Message
                    ),

                _ => throw new InvalidOperationException(
                    "Unsupported widget data result."
                )
            };
        })
        .WithName("GetWidgetData")
        .Produces<GetWidgetDataResult.Success>()
        .ProducesProblem(
            StatusCodes.Status409Conflict
        )
        .ProducesProblem(
            StatusCodes.Status400BadRequest
        )
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );

        widgets.MapPut("", async (
            Guid projectId,
            Guid dashboardId,
            UpdateDashboardWidgetsRequest request,
            UpdateDashboardWidgetsHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The authenticated user identifier is missing or invalid."
                );
            }

            if (request.Widgets is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid dashboard layout",
                    detail: "The widgets collection is required."
                );
            }

            var dashboardWidgetUpdates = new List<DashboardWidgetUpdate>(request.Widgets.Count);

            foreach (var updateRequest in request.Widgets)
            {
                if (updateRequest is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid update request",
                        detail: "At least one of the update requests was null."
                    );
                }

                if (updateRequest.Layout is not { } layoutRequest)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid dashboard layout",
                        detail: "Each widget must have a valid layout."
                    );
                }

                if (string.IsNullOrWhiteSpace(updateRequest.Title))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget update request",
                        detail: "Widget title is required."
                    );
                }

                if (string.IsNullOrWhiteSpace(updateRequest.GroupByColumn))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget update request",
                        detail: "Widget group column is required."
                    );
                }

                if (string.IsNullOrWhiteSpace(updateRequest.MeasureColumn))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget update request",
                        detail: "Widget measure column is required."
                    );
                }

                if (!Enum.IsDefined(updateRequest.Type))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget update request",
                        detail: "Invalid widget type."
                    );
                }

                if (!Enum.TryParse<AggregationType>(
                    updateRequest.Aggregation,
                    ignoreCase: true,
                    out var aggregation)
                || !Enum.IsDefined(aggregation))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget update request",
                        detail: "Invalid widget aggregation type."
                    );
                }

                try
                {
                    var layout = new WidgetLayout(
                        layoutRequest.X,
                        layoutRequest.Y,
                        layoutRequest.Width,
                        layoutRequest.Height
                    );

                    dashboardWidgetUpdates.Add(new DashboardWidgetUpdate(
                        updateRequest.WidgetId,
                        updateRequest.DatasetId,
                        updateRequest.Type,
                        updateRequest.Title,
                        updateRequest.GroupByColumn,
                        updateRequest.MeasureColumn,
                        aggregation,
                        layout
                    ));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid dashboard layout",
                        detail: "Each widget must have a valid layout."
                    );
                }
            }

            if (request.ExpectedRevision is not { } expectedRevision || expectedRevision < 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid dashboard revision value",
                    detail: "Revision is null or negative."
                );
            }

            var command = new UpdateDashboardWidgetsCommand(
                projectId,
                dashboardId,
                ownerId,
                dashboardWidgetUpdates,
                expectedRevision
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                UpdateDashboardWidgetsResult.Success success =>
                    Results.Ok(new UpdateDashboardWidgetsResponse(success.CurrentRevision)),

                UpdateDashboardWidgetsResult.DashboardNotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Dashboard not found",
                        detail: "The dashboard does not exist or is not accessible by the current user."
                    ),

                UpdateDashboardWidgetsResult.InvalidWidgetSet =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget set.",
                        detail: "Widget IDs must be non-empty and unique."
                    ),

                UpdateDashboardWidgetsResult.InvalidConfiguration =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid widget configuration",
                        detail: "One or more widget configurations are invalid."
                    ),

                UpdateDashboardWidgetsResult.DatasetNotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Dataset not found",
                        detail: "One or more datasets do not exist or are not accessible."
                    ),

                UpdateDashboardWidgetsResult.RevisionConflict conflict =>
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Dashboard revision conflict.",
                        detail: "The dashboard has been modified since it was last loaded. " +
                                "The latest version must be retrieved before saving again.",
                        extensions: new Dictionary<string, object?>
                        {
                            ["expectedRevision"] = conflict.ExpectedRevision,
                            ["currentRevision"] = conflict.CurrentRevision
                        }
                    ),

                UpdateDashboardWidgetsResult.LayoutCollision =>
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Widget layout collision.",
                        detail: "Two or more widgets overlap. Widget layouts must not overlap."
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("UpdateDashboardWidgets")
        .Produces<UpdateDashboardWidgetsResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
