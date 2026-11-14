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
            var failed = await _importJobRepository.TryFailAsync(
                workItem,
                CreateErrorMessage(exception),
                cancellationToken
            );

            if (failed)
            {
                _logger.LogWarning(
                    exception,
                    "Import job {ImportJobId} attempt {AttemptNumber} failed.",
                    job.Id,
                    workItem.AttemptNumber
                );
            }
            else
            {
                LogOwnershipLost(workItem);
            }

            return true;
        }

        try
        {
            var outcome = await _importJobRepository.CompleteAsync(
                workItem,
                importResult.StorageKey,
                importResult.RowCount,
                importResult.ColumnCount,
                cancellationToken
            );

            if (outcome == ImportJobCompletionOutcome.OwnershipLost)
            {
                LogOwnershipLost(workItem);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Verify the exact committed result; never retry publication or delete a possibly published artifact.
            var completed = await _importJobRepository.IsCompletedAsync(
                workItem,
                importResult.StorageKey,
                importResult.RowCount,
                importResult.ColumnCount,
                cancellationToken
            );

            if (!completed)
            {
                throw;
            }

            _logger.LogWarning(
                exception,
                "Import job {ImportJobId} attempt {AttemptNumber} completion was confirmed after a database error.",
                job.Id,
                workItem.AttemptNumber
            );
        }

        return true;
    }

    private void LogOwnershipLost(ImportJobWorkItem workItem)
    {
        _logger.LogInformation(
            "Import job {ImportJobId} attempt {AttemptNumber} no longer owns the job; its result was not applied.",
            workItem.Job.Id,
            workItem.AttemptNumber
        );
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
