using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Dashboards;

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

            if (result is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Project not found",
                    detail: "The project does not exist or is not accessible by the current user."
                );
            }

            return Results.Created(
                $"/projects/{projectId}/dashboards/{result.Id}",
                result
            );
        })
        .WithName("CreateDashboard")
        .Produces<CreateDashboardResponse>(
            StatusCodes.Status201Created
        )
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );

        dashboards.MapGet("", async (
            Guid projectId,
            GetDashboardsHandler handler,
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

            var query = new GetDashboardsQuery(
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return Results.Ok(result);
        })
        .WithName("GetDashboards")
        .Produces<IReadOnlyList<GetDashboardsResponse>>();

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
        .ProducesProblem(
            StatusCodes.Status404NotFound
        );

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
