using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportJobWorker> _logger;

    public ImportJobWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ImportJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await using var recoveryScope = _scopeFactory.CreateAsyncScope();

            var recoveryService = recoveryScope.ServiceProvider
                .GetRequiredService<ImportJobRecoveryService>();

            await recoveryService.RecoverInterruptedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected failure during import jobs recovery."
            );

            throw;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var jobScope = _scopeFactory.CreateAsyncScope();

                var processor = jobScope.ServiceProvider
                    .GetRequiredService<ImportJobProcessor>();

                var processed =
                    await processor.ProcessNextAsync(
                        stoppingToken
                    );

                if (!processed)
                {
                    await Task.Delay(
                        IdleDelay,
                        stoppingToken
                    );
                }
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
                    "Unexpected failure in the import job worker."
                );

                await Task.Delay(
                    IdleDelay,
                    stoppingToken
                );
            }
        }
    }
}
