using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportJobOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportJobWorker> _logger;

    public ImportJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ImportJobOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var slots = new Task[_options.MaxConcurrentImports];
        for (var index = 0; index < slots.Length; index++)
        {
            slots[index] = RunSlotAsync(index, stoppingToken);
        }

        return Task.WhenAll(slots);
    }

    private async Task RunSlotAsync(int slot, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                await using var jobScope = _scopeFactory.CreateAsyncScope();
                var processor = jobScope.ServiceProvider.GetRequiredService<ImportJobProcessor>();
                processed = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Unexpected failure in import worker slot {Slot}. ErrorType {ErrorType}.",
                    slot,
                    exception.GetType().Name
                );
                processed = false;
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(IdleDelay, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
