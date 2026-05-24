using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;

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
    }
}