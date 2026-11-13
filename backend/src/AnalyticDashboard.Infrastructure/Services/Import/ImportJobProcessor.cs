using AnalyticDashboard.Application.Datasets.Persistence;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobProcessor
{
    private const int MaxErrorMessageLength = 2000;

    private readonly IImportJobRepository _importJobRepository;
    private readonly DuckDbCsvImporter _csvImporter;
    private readonly ILogger<ImportJobProcessor> _logger;

    public ImportJobProcessor(
        IImportJobRepository importJobRepository,
        DuckDbCsvImporter csvImporter,
        ILogger<ImportJobProcessor> logger)
    {
        _importJobRepository = importJobRepository;
        _csvImporter = csvImporter;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var workItem = await _importJobRepository.ClaimNextPendingAsync(
            cancellationToken
        );

        if (workItem is null)
        {
            return false;
        }

        var job = workItem.Job;
        var version = workItem.Version;
        var dataset = workItem.Dataset;

        var resultStorageDirectory = $"datasets/{dataset.Id:N}/versions/{version.Id:N}/attempts";

        DuckDbCsvImportResult importResult;

        try
        {
            importResult = await _csvImporter.ImportAsync(
                job.SourceStorageKey,
                resultStorageDirectory,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            version.MarkFailed();

            job.MarkFailed(
                CreateErrorMessage(exception)
            );

            await _importJobRepository.SaveChangesAsync(
                cancellationToken
            );

            _logger.LogWarning(
                exception,
                "Import job {ImportJobId} failed.",
                job.Id
            );

            return true;
        }

        version.MarkReady(
            importResult.StorageKey,
            importResult.RowCount,
            importResult.ColumnCount
        );

        dataset.PublishVersion(
            version.Id
        );

        job.MarkCompleted();

        // A failed acknowledgement does not prove that publication was rolled back.
        await _importJobRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string CreateErrorMessage(
        Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(
            exception.Message
        )
            ? exception.GetType().Name
            : exception.Message;

        return message.Length <= MaxErrorMessageLength
            ? message
            : message[..MaxErrorMessageLength];
    }
}
