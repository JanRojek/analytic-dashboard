using System.Diagnostics;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Widgets.CreateWidget;
using AnalyticDashboard.Application.Widgets.DeleteWidget;
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

        widgets.MapPost("", async (
            Guid projectId,
            Guid dashboardId,
            CreateWidgetRequest request,
            CreateWidgetHandler handler,
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

            if (request.DatasetId == Guid.Empty)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "DatasetId cannot be empty."
                );
            }

            if (!Enum.IsDefined(request.Type))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "The widget type is not supported."
                );
            }

            if (!Enum.TryParse<AggregationType>(
                    request.Aggregation,
                    ignoreCase: true,
                    out var aggregation)
                || !Enum.IsDefined(aggregation))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "Aggregation must be one of: Sum, Average, Min, Max, Count."
                );
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "Widget title cannot be empty."
                );
            }

            if (string.IsNullOrWhiteSpace(request.GroupByColumn))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "Group by column cannot be empty."
                );
            }

            if (string.IsNullOrWhiteSpace(request.MeasureColumn))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "Measure column cannot be empty."
                );
            }

            if (request.Layout == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget configuration",
                    detail: "Widget layout cannot be null."
                );
            }

            WidgetLayout layout;

            try
            {
                layout = new WidgetLayout(
                    request.Layout.X,
                    request.Layout.Y,
                    request.Layout.Width,
                    request.Layout.Height
                );
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid widget layout",
                    detail: ex.Message
                );
            }

            var command = new CreateWidgetCommand(
                projectId,
                ownerId,
                dashboardId,
                request.DatasetId,
                request.Type,
                request.Title,
                request.GroupByColumn,
                request.MeasureColumn,
                aggregation,
                layout
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            if (result is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Widget resources not found",
                    detail: "The dashboard or dataset does not exist or is not accessible by the current user."
                );
            }

            return Results.Created(
                $"/projects/{projectId}/dashboards/{dashboardId}/widgets/{result.Id}",
                result
            );
        })
        .WithName("CreateWidget")
        .Produces<CreateWidgetResponse>(
            StatusCodes.Status201Created
        )
        .ProducesProblem(
            StatusCodes.Status400BadRequest
        )
        .ProducesProblem(
            StatusCodes.Status404NotFound
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

        widgets.MapDelete("/{widgetId:guid}", async (
            Guid projectId,
            Guid dashboardId,
            Guid widgetId,
            DeleteWidgetHandler handler,
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

            var command = new DeleteWidgetCommand(
                widgetId,
                dashboardId,
                projectId,
                ownerId
            );

            var success = await handler.HandleAsync(
                command,
                cancellationToken
            );

            if (!success)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Widget not found",
                    detail: "The widget does not exist or is not accessible by the current user."
                );
            }

            return Results.NoContent();
        })
        .WithName("DeleteWidget")
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );

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

            var command = new UpdateDashboardWidgetsCommand(
                projectId,
                dashboardId,
                ownerId,
                dashboardWidgetUpdates
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                UpdateDashboardWidgetsResult.Success =>
                    Results.NoContent(),

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
                        detail: "Provided widget IDs do not match the dashboard widgets."
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
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
