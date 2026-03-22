using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;

namespace AnalyticDashboard.Api.Endpoints;

public static class DatasetEndpoints
{
    public static IEndpointRouteBuilder MapDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/datasets/import/csv", async (
            IFormFile file,
            ImportCsvDatasetHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { message = "File is empty." });
            }

            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "File is not a CSV file." });
            }

            var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "storage", "datasets");
            Directory.CreateDirectory(storagePath);
            
            var datasetId = Guid.NewGuid();
            var storedFileName = $"{datasetId}.csv";
            var storedFilePath = Path.Combine(storagePath, storedFileName);

            await using (var stream = new FileStream(storedFilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var lines = await File.ReadAllLinesAsync(storedFilePath, cancellationToken);
            var nonEmptyLines = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (nonEmptyLines.Count == 0)
            {
                return Results.BadRequest(new { message = "CSV file has no data." });
            }

            var headerColumns = nonEmptyLines[0].Split(';', StringSplitOptions.TrimEntries);
            var columnCount = headerColumns.Length;
            var rowCount = Math.Max(0, nonEmptyLines.Count - 1);

            var command = new ImportCsvDatasetCommand(
                datasetId,
                Path.GetFileNameWithoutExtension(file.FileName),
                file.FileName,
                storedFilePath,
                rowCount,
                columnCount
            );

            var result = await handler.Handle(command, cancellationToken);

            return Results.Created($"/datasets/{result.Id}", result);
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