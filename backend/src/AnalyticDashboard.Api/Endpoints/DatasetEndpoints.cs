using System.Diagnostics;
using System.Security.Claims;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasetProfile;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;

namespace AnalyticDashboard.Api.Endpoints;

public static class DatasetEndpoints
{
    public static IEndpointRouteBuilder MapDatasetEndpoints(
        this IEndpointRouteBuilder app)
    {
        var datasets = app
            .MapGroup("/projects/{projectId:guid}/datasets")
            .WithTags("Datasets")
            .RequireAuthorization()
            .ProducesProblem(
                StatusCodes.Status500InternalServerError
            );

        datasets.MapPost("/import/csv", async (
            Guid projectId,
            IFormFile file,
            ImportCsvDatasetHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            if (file.Length == 0)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["File"] = ["File cannot be empty."]
                    }
                );
            }

            await using var stream = file.OpenReadStream();

            var command = new ImportCsvDatasetCommand(
                projectId,
                ownerId,
                stream,
                file.FileName
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                ImportCsvDatasetResult.Accepted accepted =>
                    Results.Accepted(
                        value: new ImportCsvDatasetResponse(
                            accepted.DatasetId,
                            accepted.DatasetVersionId,
                            accepted.ImportJobId
                        )
                    ),

                ImportCsvDatasetResult.ProjectNotFound =>
                    Results.NotFound(),

                ImportCsvDatasetResult.InvalidFile invalid =>
                    Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["File"] = [invalid.Message]
                        }
                    ),

                _ => throw new UnreachableException()
            };
        })
        .WithName("ImportCsvDataset")
        .DisableAntiforgery()
        .Produces<ImportCsvDatasetResponse>(
            StatusCodes.Status202Accepted
        )
        .ProducesValidationProblem()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        );

        datasets.MapGet("", async (
            Guid projectId,
            GetDatasetsHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetDatasetsQuery(
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetDatasetsResult.Success success =>
                    Results.Ok(
                        success.Items
                            .Select(item =>
                                new DatasetResponse(
                                    item.Id,
                                    item.Name,
                                    item.CreatedAtUtc,
                                    item.Version is null
                                        ? null
                                        : new DatasetCurrentVersionResponse(
                                            item.Version.Id,
                                            item.Version.VersionNumber,
                                            item.Version.OriginalFileName,
                                            item.Version.RowCount,
                                            item.Version.ColumnCount
                                        )
                                )
                            )
                            .ToList()
                    ),

                GetDatasetsResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetDatasets")
        .Produces<IReadOnlyList<DatasetResponse>>()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        );

        datasets.MapGet("/{datasetId:guid}", async (
            Guid projectId,
            Guid datasetId,
            GetDatasetByIdHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetDatasetByIdQuery(
                datasetId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetDatasetByIdResult.Found found =>
                    Results.Ok(
                        new DatasetResponse(
                            found.Id,
                            found.Name,
                            found.CreatedAtUtc,
                            found.Version is null
                                ? null
                                : new DatasetCurrentVersionResponse(
                                    found.Version.Id,
                                    found.Version.VersionNumber,
                                    found.Version.OriginalFileName,
                                    found.Version.RowCount,
                                    found.Version.ColumnCount
                                )
                        )
                    ),

                GetDatasetByIdResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetDatasetById")
        .Produces<DatasetResponse>()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        );

        datasets.MapDelete("/{datasetId:guid}", async (
            Guid projectId,
            Guid datasetId,
            DeleteDatasetHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var command = new DeleteDatasetCommand(
                datasetId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                command,
                cancellationToken
            );

            return result switch
            {
                DeleteDatasetResult.Success =>
                    Results.NoContent(),

                DeleteDatasetResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("DeleteDataset")
        .Produces(
            StatusCodes.Status204NoContent
        )
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        );

        datasets.MapGet("/{datasetId:guid}/profile", async (
            Guid projectId,
            Guid datasetId,
            GetDatasetProfileHandler handler,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            if (!user.TryGetUserId(out var ownerId))
            {
                return Results.Unauthorized();
            }

            var query = new GetDatasetProfileQuery(
                datasetId,
                projectId,
                ownerId
            );

            var result = await handler.HandleAsync(
                query,
                cancellationToken
            );

            return result switch
            {
                GetDatasetProfileResult.Found found =>
                    Results.Ok(
                        new DatasetProfileResponse(
                            found.Profile.Id,
                            found.Profile.Name,
                            found.Profile.OriginalFileName,
                            found.Profile.RowCount,
                            found.Profile.ColumnCount,
                            found.Profile.Columns
                                .Select(column =>
                                    new DatasetColumnProfileResponse(
                                        column.Name,
                                        column.Type,
                                        column.NullCount,
                                        column.Min,
                                        column.Max,
                                        column.Avg
                                    )
                                )
                                .ToList(),
                            found.Profile.PreviewRows
                        )
                    ),

                GetDatasetProfileResult.NotFound =>
                    Results.NotFound(),

                _ => throw new UnreachableException()
            };
        })
        .WithName("GetDatasetProfile")
        .Produces<DatasetProfileResponse>()
        .Produces(
            StatusCodes.Status401Unauthorized
        )
        .Produces(
            StatusCodes.Status404NotFound
        );

        return app;
    }
}
