using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageCleanupProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<StorageCleanupProcessor> _logger;

    public StorageCleanupProcessor(
        AppDbContext dbContext,
        IFileStorage fileStorage,
        ILogger<StorageCleanupProcessor> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var job = await _dbContext.StorageCleanupJobs
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

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                exception,
                "Failed to clean up dataset storage {DatasetId}. Attempt {AttemptCount}.",
                job.DatasetId,
                job.AttemptCount
            );

            return true;
        }

        _dbContext.StorageCleanupJobs.Remove(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Cleaned up storage for dataset {DatasetId}.",
            job.DatasetId
        );

        return true;
    }
}
