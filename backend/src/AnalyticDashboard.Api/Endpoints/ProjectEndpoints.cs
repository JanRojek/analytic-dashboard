using System.Diagnostics;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Application.Projects.CreateProject;
using AnalyticDashboard.Application.Projects.GetProjectById;
using AnalyticDashboard.Application.Projects.GetProjects;
using AnalyticDashboard.Application.Projects.RenameProject;
using AnalyticDashboard.Application.Projects.DeleteProject;

namespace AnalyticDashboard.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(
        this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        projects.MapPost("", async (
            CreateProjectRequest request,
            CreateProjectHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
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
                        new CreateProjectResponse(
                            success.Id,
                            success.Name,
                            success.CreatedAtUtc
                        )
                    ),

                CreateProjectResult.NameAlreadyExists conflict =>
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Project name already exists.",
                        detail: $"Project '{conflict.ConflictingName}' already exists."
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
        .Produces<CreateProjectResponse>(
            StatusCodes.Status201Created
        )
        .ProducesValidationProblem()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .ProducesProblem(
            StatusCodes.Status409Conflict
        )
        .ProducesProblem(
            StatusCodes.Status500InternalServerError
        );

        projects.MapGet("/{id:guid}", async (
            Guid id,
            GetProjectByIdHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
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
                            found.CreatedAtUtc
                        )
                    ),

                GetProjectByIdResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetProjectById")
        .Produces<GetProjectByIdResponse>()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        )
        .ProducesProblem(
            StatusCodes.Status500InternalServerError
        );

        projects.MapGet("", async (
            GetProjectsHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken,
            int page = GetProjectsQuery.DefaultPage,
            int pageSize = GetProjectsQuery.DefaultPageSize) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetProjectsQuery(
                ownerId,
                page,
                pageSize
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetProjectsResult.Success success =>
                    Results.Ok(
                        new GetProjectsResponse(
                            success.Items
                                .Select(item => new GetProjectsResponseItem(
                                    item.Id,
                                    item.Name,
                                    item.CreatedAtUtc
                                ))
                                .ToList(),
                            success.Page,
                            success.PageSize,
                            success.TotalCount,
                            success.TotalPages
                        )
                    ),

                GetProjectsResult.InvalidPageSize invalid =>
                    Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["PageSize"] = [invalid.Message]
                        }
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetProjects")
        .Produces<GetProjectsResponse>()
        .ProducesValidationProblem()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .ProducesProblem(
            StatusCodes.Status500InternalServerError
        );

        projects.MapPatch("/{id:guid}", async (
            Guid id,
            RenameProjectRequest request,
            RenameProjectHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var command = new RenameProjectCommand(
                id,
                ownerId,
                request.Name
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                RenameProjectResult.Success success =>
                    Results.Ok(
                        new RenameProjectResponse(
                            success.Id,
                            success.Name,
                            success.CreatedAtUtc
                        )
                    ),

                RenameProjectResult.NameAlreadyExists conflict =>
                    Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Project name already exists.",
                        detail: $"Project '{conflict.ConflictingName}' already exists."
                    ),

                RenameProjectResult.InvalidName invalid =>
                    Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Name"] = [invalid.Message]
                        }
                    ),

                RenameProjectResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("RenameProject")
        .Produces<RenameProjectResponse>()
        .ProducesValidationProblem()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        )
        .ProducesProblem(
            StatusCodes.Status409Conflict
        )
        .ProducesProblem(
            StatusCodes.Status500InternalServerError
        );

        projects.MapDelete("/{id:guid}", async (
            Guid id,
            DeleteProjectHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var command = new DeleteProjectCommand(
                id,
                ownerId
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                DeleteProjectResult.Success =>
                    Results.NoContent(),

                DeleteProjectResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("DeleteProject")
        .Produces(
            StatusCodes.Status204NoContent
        )
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        )
        .ProducesProblem(
            StatusCodes.Status500InternalServerError
        );

        return app;
    }
}
