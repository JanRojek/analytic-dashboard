using System.Security.Claims;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Application.Projects.CreateProject;

namespace AnalyticDashboard.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/projects", async (
            CreateProjectRequest request,
            CreateProjectHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var ownerIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(ownerIdClaim, out var ownerId))
            {
                return Results.Unauthorized();
            }

            var command = new CreateProjectCommand(
                ownerId,
                request.Name
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                CreateProjectResult.Success success =>
                    Results.Created(
                        $"/projects/{success.Id}",
                        success
                    ),

                CreateProjectResult.NameAlreadyExists conflict =>
                    Results.Conflict(new
                    {
                        message = $"Project '{conflict.RequestedName}' already exists."
                    }),

                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        })
        .WithName("CreateProject")
        .WithTags("Projects")
        .RequireAuthorization();

        return app;
    }
}
