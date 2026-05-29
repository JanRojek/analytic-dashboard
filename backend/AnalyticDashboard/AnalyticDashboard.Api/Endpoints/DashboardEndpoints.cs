using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;
using AnalyticDashboard.Application.Widgets.CreateWidget;
using AnalyticDashboard.Application.Widgets.DeleteWidget;
using AnalyticDashboard.Application.Widgets.GetWidgets;

namespace AnalyticDashboard.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dashboards", async (
                CreateDashboardCommand command,
                CreateDashboardHandler handler,
                CancellationToken cancellationToken) =>
        {
                var result = await handler.Handle(command, cancellationToken);

                return Results.Created($"/dashboards/{result.Id}", result);
        })
        .WithName("CreateDashboard")
        .WithTags("Dashboards")
        .RequireAuthorization();
        
        app.MapGet("/dashboards", async (
            GetDashboardsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDashboardsQuery();

            var result = await handler.Handle(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetDashboards")
        .WithTags("Dashboards")
        .RequireAuthorization();
        
        app.MapGet("/dashboards/{id:guid}", async (
            Guid id,
            GetDashboardByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDashboardByIdQuery(id);
            
            var result = await handler.Handle(query, cancellationToken);
            
            if (result is null)
            {
                return Results.NotFound(new { message = $"Dashboard with ID {id} doesn't exist." });
            }

            return Results.Ok(result);
        })
        .WithName("GetDashboardById")
        .WithTags("Dashboards")
        .RequireAuthorization();
        
        app.MapDelete("/dashboards/{id:guid}", async (
            Guid id,
            DeleteDashboardHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteDashboardCommand(id);
            
            var success = await handler.Handle(command, cancellationToken);

            return success ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDashboard")
        .WithTags("Dashboards")
        .RequireAuthorization();
        
        app.MapPost("/dashboards/{dashboardId:guid}/widgets", async (
            Guid dashboardId,
            CreateWidgetRequest request,
            CreateWidgetHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateWidgetCommand(
                dashboardId,
                request.Type,
                request.Title,
                request.XColumn,
                request.YColumn,
                request.Aggregation
            );

            var result = await handler.Handle(command, cancellationToken);

            return Results.Created(
                $"/dashboards/{dashboardId}/widgets/{result.Id}",
                result
            );
        })
        .WithName("CreateWidget")
        .WithTags("Widgets")
        .RequireAuthorization();
        
        app.MapGet("/dashboards/{dashboardId:guid}/widgets", async (
            Guid dashboardId,
            GetWidgetsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetWidgetsQuery(dashboardId);

            var result = await handler.Handle(query, cancellationToken);

            return Results.Ok(result);
        })
        .WithName("GetWidgets")
        .WithTags("Widgets")
        .RequireAuthorization();
        
        app.MapDelete("/widgets/{id:guid}", async (
            Guid id,
            DeleteWidgetHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteWidgetCommand(id);

            var success = await handler.Handle(command, cancellationToken);

            return success ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteWidget")
        .WithTags("Widgets")
        .RequireAuthorization();
    }
}