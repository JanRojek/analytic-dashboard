using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobProcessor
{
    private const int MaxErrorMessageLength = 2000;

    private readonly IImportJobRepository _importJobRepository;
    private readonly DuckDbCsvImporter _csvImporter;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<ImportJobProcessor> _logger;

    public ImportJobProcessor(
        IImportJobRepository importJobRepository,
        DuckDbCsvImporter csvImporter,
        IFileStorage fileStorage,
        ILogger<ImportJobProcessor> logger)
    {
        _importJobRepository = importJobRepository;
        _csvImporter = csvImporter;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var workItem =
            await _importJobRepository.GetNextPendingAsync(
                cancellationToken
            );

        if (workItem is null)
        {
            return false;
        }

        var job = workItem.Job;
        var version = workItem.Version;
        var dataset = workItem.Dataset;

        var resultStorageKey =
            $"datasets/{dataset.Id:N}/versions/{version.Id:N}/data.parquet";

        job.MarkRunning();

        DuckDbCsvImportResult importResult;

        try
        {
            importResult = await _csvImporter.ImportAsync(
                job.SourceStorageKey,
                resultStorageKey,
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

        try
        {
            await _importJobRepository.SaveChangesAsync(
                cancellationToken
            );
        }
        catch
        {
            try
            {
                await _fileStorage.DeleteAsync(
                    importResult.StorageKey,
                    CancellationToken.None
                );
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Failed to clean up Parquet artifact {StorageKey} after database finalization failure.",
                    importResult.StorageKey
                );
            }

            throw;
        }

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
