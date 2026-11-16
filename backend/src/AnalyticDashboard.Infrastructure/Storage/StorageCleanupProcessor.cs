using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageCleanupProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IStorageNamespaceCloser _namespaceCloser;
    private readonly ILogger<StorageCleanupProcessor> _logger;

    public StorageCleanupProcessor(
        AppDbContext dbContext,
        IFileStorage fileStorage,
        IStorageNamespaceCloser namespaceCloser,
        ILogger<StorageCleanupProcessor> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _namespaceCloser = namespaceCloser;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var job = await _dbContext.StorageCleanupJobs
            .AsNoTracking()
            .Where(job => job.NextAttemptAtUtc <= now)
            .OrderBy(job => job.NextAttemptAtUtc)
            .ThenBy(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        var storageKey = $"datasets/{job.DatasetId:N}";

        try
        {
            await _namespaceCloser.CloseNamespaceAsync(
                job.DatasetId,
                cancellationToken
            );

            await _fileStorage.DeleteDirectoryAsync(
                storageKey,
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
            var exponent = Math.Min(job.AttemptCount, 6);
            var delay = TimeSpan.FromSeconds(30 * (1 << exponent));

            job.ScheduleRetry(delay);

            await _dbContext.StorageCleanupJobs
                .Where(item => item.Id == job.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.AttemptCount, job.AttemptCount)
                        .SetProperty(item => item.NextAttemptAtUtc, job.NextAttemptAtUtc),
                    cancellationToken
                );

            _logger.LogWarning(
                exception,
                "Failed to clean up dataset storage {DatasetId}. Attempt {AttemptCount}.",
                job.DatasetId,
                job.AttemptCount
            );

            return true;
        }

        await _dbContext.StorageCleanupJobs
            .Where(item => item.Id == job.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "Closed namespace and cleaned up storage for dataset {DatasetId}.",
            job.DatasetId
        );

        return true;
    }
}
