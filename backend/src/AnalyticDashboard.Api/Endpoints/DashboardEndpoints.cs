using System.Diagnostics;
using System.Security.Claims;

using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Application.Common.Pagination;
using AnalyticDashboard.Application.Dashboards.GetDashboardSnapshot;

namespace AnalyticDashboard.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(
        this IEndpointRouteBuilder app)
    {
        var dashboards = app
            .MapGroup("/projects/{projectId:guid}/dashboards")
            .WithTags("Dashboards")
            .RequireAuthorization()
            .ProducesProblem(
                StatusCodes.Status401Unauthorized
            )
            .ProducesProblem(
                StatusCodes.Status500InternalServerError
            );

        dashboards.MapPost("", async (
            Guid projectId,
            CreateDashboardRequest request,
            CreateDashboardHandler handler,
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

            var command = new CreateDashboardCommand(
                projectId,
                ownerId,
                request.Name
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                CreateDashboardResult.Success success =>
                    Results.Created(
                        $"/projects/{projectId}/dashboards/{success.Id}",
                        new CreateDashboardResponse(success.Id)
                    ),

                CreateDashboardResult.ProjectNotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Project not found",
                        detail: "The project does not exist or is not accessible by the current user."
                    ),

                CreateDashboardResult.InvalidName invalidName =>
                    Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Name"] = [invalidName.Message]
                        }
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("CreateDashboard")
        .Produces<CreateDashboardResponse>(
            StatusCodes.Status201Created
        )
        .ProducesValidationProblem()
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );


        dashboards.MapGet("", async (
            Guid projectId,
            GetDashboardsHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken,
            int page = PaginationRequest.DefaultPage,
            int pageSize = PaginationRequest.DefaultPageSize) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The authenticated user identifier is missing or invalid."
                );
            }

            var query = new GetDashboardsQuery(
                projectId,
                ownerId,
                new PaginationRequest(page, pageSize)
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetDashboardsResult.Success success =>
                    Results.Ok(
                        new GetDashboardsResponse(
                            success.Data.Items
                                .Select(item => new GetDashboardsResponseItem(
                                    item.Id,
                                    item.ProjectId,
                                    item.Name,
                                    item.CreatedAtUtc
                                ))
                                .ToList(),
                            success.Data.Page,
                            success.Data.PageSize,
                            success.Data.TotalCount,
                            success.Data.TotalPages
                        )
                    ),

                GetDashboardsResult.NotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Project not found",
                        detail: "The project does not exist or is not accessible by the current user."
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetDashboards")
        .Produces<GetDashboardsResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        dashboards.MapGet("/{dashboardId:guid}", async (
            Guid projectId,
            Guid dashboardId,
            GetDashboardByIdHandler handler,
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

            var query = new GetDashboardByIdQuery(
                dashboardId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            if (result is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Dashboard not found",
                    detail: "The dashboard does not exist or is not accessible by the current user."
                );
            }

            return Results.Ok(result);
        })
        .WithName("GetDashboardById")
        .Produces<GetDashboardByIdResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        dashboards.MapGet("/{dashboardId:guid}/editor-state", async (
            Guid projectId,
            Guid dashboardId,
            GetDashboardSnapshotHandler handler,
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

            var query = new GetDashboardSnapshotQuery(
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
                GetDashboardSnapshotResult.Success success =>
                    Results.Ok(new GetDashboardSnapshotResponse(
                        success.DashboardId,
                        success.Name,
                        success.Revision,
                        success.Widgets
                            .Select(widget => new WidgetSnapshotResponse(
                                widget.Id,
                                widget.DatasetId,
                                widget.Type,
                                widget.Title,
                                widget.GroupByColumn,
                                widget.MeasureColumn,
                                widget.Aggregation,
                                new WidgetSnapshotLayoutResponse(
                                    widget.Layout.X,
                                    widget.Layout.Y,
                                    widget.Layout.Width,
                                    widget.Layout.Height
                                ),
                                widget.CreatedAtUtc
                            ))
                            .ToArray()
                    )),

                GetDashboardSnapshotResult.NotFound =>
                    Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Dashboard not found",
                        detail: "The dashboard does not exist or is not accessible by the current user."
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetDashboardSnapshot")
        .Produces<GetDashboardSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        dashboards.MapDelete("/{dashboardId:guid}", async (
            Guid projectId,
            Guid dashboardId,
            DeleteDashboardHandler handler,
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

            var command = new DeleteDashboardCommand(
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
                    title: "Dashboard not found",
                    detail: "The dashboard does not exist or is not accessible by the current user."
                );
            }

            return Results.NoContent();
        })
        .WithName("DeleteDashboard")
        .Produces(
            StatusCodes.Status204NoContent
        )
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );
        return app;
    }
}
