using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageCleanupWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(15);

    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorageCleanupWorker> _logger;

    public StorageCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StorageCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            do
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Storage cleanup batch failed."
                    );
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested) {}
    }

    private async Task ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var processor = scope.ServiceProvider
            .GetRequiredService<StorageCleanupProcessor>();

        for (var i = 0; i < BatchSize; i++)
        {
            var processed = await processor.ProcessNextAsync(
                cancellationToken
            );

            if (!processed)
            {
                break;
            }
        }
    }
}
