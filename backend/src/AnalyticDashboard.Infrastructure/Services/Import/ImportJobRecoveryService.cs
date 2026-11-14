using AnalyticDashboard.Application.Datasets.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobRecoveryService : BackgroundService
{
    private const int RecoveryBatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportJobOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportJobRecoveryService> _logger;

    public ImportJobRecoveryService(
        IServiceScopeFactory scopeFactory,
        IOptions<ImportJobOptions> options,
        TimeProvider timeProvider,
        ILogger<ImportJobRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RecoverExpiredAsync(
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < RecoveryBatchSize; index++)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
            if (!await repository.RecoverNextExpiredAsync(cancellationToken))
            {
                break;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval, _timeProvider);
        do
        {
            try
            {
                await RecoverExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Import lease recovery failed; it will be retried on the next pass.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
