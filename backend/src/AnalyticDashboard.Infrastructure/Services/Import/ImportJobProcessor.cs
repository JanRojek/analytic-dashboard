using AnalyticDashboard.Application.Datasets.Persistence;

using DuckDB.NET.Data;
using DuckDB.NET.Native;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobProcessor
{
    private const int MaxErrorMessageLength = 2000;

    private readonly IImportJobRepository _importJobRepository;
    private readonly DuckDbCsvImporter _csvImporter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportJobOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportJobProcessor> _logger;

    public ImportJobProcessor(
        IImportJobRepository importJobRepository,
        DuckDbCsvImporter csvImporter,
        IServiceScopeFactory scopeFactory,
        IOptions<ImportJobOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportJobProcessor> logger)
    {
        _importJobRepository = importJobRepository;
        _csvImporter = csvImporter;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
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

        using var ownershipLost = new CancellationTokenSource();
        using var conversionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RunHeartbeatAsync(
            workItem,
            ownershipLost,
            conversionCancellation,
            heartbeatCancellation.Token
        );

        bool processed;
        try
        {
            processed = await ProcessClaimedAsync(
                workItem,
                ownershipLost.Token,
                conversionCancellation.Token,
                cancellationToken
            );
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            await heartbeat;
        }

        // A heartbeat left running could undo the shorter retry cooldown.
        if (!processed && !await _importJobRepository.TryAbandonAsync(workItem, cancellationToken))
        {
            LogOwnershipLost(workItem);
        }

        return true;
    }

    private async Task<bool> ProcessClaimedAsync(
        ImportJobWorkItem workItem,
        CancellationToken ownershipLost,
        CancellationToken conversionToken,
        CancellationToken cancellationToken)
    {
        var resultStorageDirectory =
            $"datasets/{workItem.Dataset.Id:N}/versions/{workItem.Version.Id:N}/attempts";
        DuckDbCsvImportResult importResult;

        try
        {
            // The provider executes native queries synchronously; keep heartbeat independent of conversion.
            importResult = await Task.Run(
                () => _csvImporter.ImportAsync(
                    workItem.Job.SourceStorageKey,
                    resultStorageDirectory,
                    conversionToken
                ),
                conversionToken
            );
            conversionToken.ThrowIfCancellationRequested();
        }
        catch (Exception) when (conversionToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogOwnershipLost(workItem);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException
            or DuckDBException { ErrorType: DuckDBErrorType.InvalidInput or DuckDBErrorType.Conversion })
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
                    "Import job {ImportJobId} attempt {AttemptNumber} failed permanently.",
                    workItem.Job.Id,
                    workItem.AttemptNumber
                );
            }
            else
            {
                LogOwnershipLost(workItem);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Import job {ImportJobId} attempt {AttemptNumber} stopped unexpectedly; lease recovery will retry after a short cooldown, within the attempt limit.",
                workItem.Job.Id,
                workItem.AttemptNumber
            );
            return false;
        }

        if (ownershipLost.IsCancellationRequested)
        {
            LogOwnershipLost(workItem);
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
            // A broken original connection cannot verify its commit. Read in a fresh scope without republishing.
            await using var verificationScope = _scopeFactory.CreateAsyncScope();
            var repository = verificationScope.ServiceProvider.GetRequiredService<IImportJobRepository>();
            var completed = await repository.IsCompletedAsync(
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
                workItem.Job.Id,
                workItem.AttemptNumber
            );
        }

        return true;
    }

    private async Task RunHeartbeatAsync(
        ImportJobWorkItem workItem,
        CancellationTokenSource ownershipLost,
        CancellationTokenSource conversionCancellation,
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(_options.HeartbeatInterval);
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var repository = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
                    if (!await repository.TryRenewLeaseAsync(workItem, timeout.Token))
                    {
                        await ownershipLost.CancelAsync();
                        await conversionCancellation.CancelAsync();
                        return;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Import job {ImportJobId} attempt {AttemptNumber} heartbeat failed; ownership is not yet known to be lost.",
                        workItem.Job.Id,
                        workItem.AttemptNumber
                    );
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
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
