using System.Security.Claims;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Application.Projects.CreateProject;
using System.Diagnostics;
using AnalyticDashboard.Application.Projects.GetProjectById;

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

            if (!Guid.TryParse(ownerIdClaim, out var ownerId)
                || ownerId == Guid.Empty)
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
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Project name already exists.",
                        detail: $"Project '{conflict.RequestedName}' already exists."
                    ),

                CreateProjectResult.InvalidName invalidName =>
                    Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Name"] = [invalidName.Message]
                        }
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("CreateProject")
        .WithTags("Projects")
        .RequireAuthorization();

        app.MapGet("/projects/{id:guid}", async (
            Guid id,
            GetProjectByIdHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var ownerIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(ownerIdClaim, out var ownerId)
                || ownerId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var query = new GetProjectByIdQuery(
                id,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetProjectByIdResult.Found found =>
                    Results.Ok(
                        new GetProjectByIdResponse(
                            found.Id,
                            found.Name,
                            found.CreatedAt
                        )
                    ),

                GetProjectByIdResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetProjectById")
        .WithTags("Projects")
        .RequireAuthorization();

        return app;
    }
}
