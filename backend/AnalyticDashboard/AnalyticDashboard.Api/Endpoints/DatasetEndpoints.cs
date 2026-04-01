using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;
using AnalyticDashboard.Application.Import;

namespace AnalyticDashboard.Api.Endpoints;

public static class DatasetEndpoints
{
    public static IEndpointRouteBuilder MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/datasets/import/csv", async (
            IFormFile file,
            ICsvImportService csvImportService,
            ImportCsvDatasetHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { message = "File is empty." });
            }

            try
            {
                await using var stream = file.OpenReadStream();

                var importResult = await csvImportService.ImportAsync(
                    stream,
                    file.FileName,
                    cancellationToken);

                var command = new ImportCsvDatasetCommand(
                    importResult.Id,
                    Path.GetFileNameWithoutExtension(file.FileName),
                    importResult.OriginalFileName,
                    importResult.StoredPath,
                    importResult.RowCount,
                    importResult.ColumnCount
                );

                var result = await handler.Handle(command, cancellationToken);

                return Results.Created($"/datasets/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ImportCsvDataset")
        .WithTags("Datasets")
        .DisableAntiforgery();

        app.MapGet("/datasets", async (
            GetDatasetsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDatasetsQuery();

            var result = await handler.Handle(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetDatasets")
        .WithTags("Datasets");

        app.MapGet("/datasets/{id}", async (
            Guid id,
            GetDatasetByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDatasetByIdQuery(id);

            var result = await handler.Handle(query, cancellationToken);

            if (result is null)
            {
                return Results.NotFound(new { message = $"Dataset with ID {id} doesn't exist." });
            }

            return Results.Ok(result);
        })
        .WithName("GetDatasetById")
        .WithTags("Datasets");

        app.MapDelete("/datasets/{id}", async (
            Guid id,
            DeleteDatasetHandler handler,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteDatasetCommand(id);

            var success = await handler.Handle(command, cancellationToken);

            return success ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDataset")
        .WithTags("Datasets");

        return app;
    }
}