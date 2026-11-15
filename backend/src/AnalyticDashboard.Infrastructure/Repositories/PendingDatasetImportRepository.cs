using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Repositories;

public sealed class PendingDatasetImportRepository : IPendingDatasetImportRepository
{
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(10);

    private readonly AppDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDatasetImportRepository> _logger;

    public PendingDatasetImportRepository(
        AppDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<PendingDatasetImportRepository> logger)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task CreateAsync(
        Dataset dataset,
        DatasetVersion version,
        ImportJob importJob,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        var commitAttempted = false;

        try
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            _dbContext.Datasets.Add(dataset);
            _dbContext.DatasetVersions.Add(version);
            _dbContext.ImportJobs.Add(importJob);

            await _dbContext.SaveChangesAsync(cancellationToken);

            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            if (!commitAttempted && await TryRollbackAsync(transaction, importJob.Id))
            {
                throw new PendingDatasetImportRolledBackException(exception);
            }

            if (await TryConfirmCreationAsync(dataset, version, importJob))
            {
                _logger.LogWarning(
                    "Import job {ImportJobId} creation was confirmed after a database error. ErrorType {ErrorType}.",
                    importJob.Id,
                    exception.GetType().Name
                );
                return;
            }

            _logger.LogWarning(
                "Import job {ImportJobId} creation outcome is unknown; retaining its source. ErrorType {ErrorType}.",
                importJob.Id,
                exception.GetType().Name
            );
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        "Import job {ImportJobId} transaction disposal failed. ErrorType {ErrorType}.",
                        importJob.Id,
                        exception.GetType().Name
                    );
                }
            }
        }
    }

    private async Task<bool> TryRollbackAsync(IDbContextTransaction? transaction, Guid importJobId)
    {
        if (transaction is null)
        {
            return true;
        }

        // Request cancellation must not prevent resolving whether source cleanup is safe.
        using var timeout = new CancellationTokenSource(ResolutionTimeout);
        try
        {
            await transaction.RollbackAsync(timeout.Token);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Import job {ImportJobId} rollback could not be confirmed. ErrorType {ErrorType}.",
                importJobId,
                exception.GetType().Name
            );
            return false;
        }
    }

    private async Task<bool> TryConfirmCreationAsync(
        Dataset dataset,
        DatasetVersion version,
        ImportJob importJob)
    {
        using var timeout = new CancellationTokenSource(ResolutionTimeout);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            return await (
                from job in context.ImportJobs.AsNoTracking()
                join storedVersion in context.DatasetVersions.AsNoTracking()
                    on job.DatasetVersionId equals storedVersion.Id
                join storedDataset in context.Datasets.AsNoTracking()
                    on storedVersion.DatasetId equals storedDataset.Id
                where job.Id == importJob.Id
                    && storedVersion.Id == version.Id
                    && storedDataset.Id == dataset.Id
                    && storedDataset.ProjectId == dataset.ProjectId
                    && job.SourceStorageKey == importJob.SourceStorageKey
                select job.Id
            ).AnyAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Import job {ImportJobId} creation verification failed. ErrorType {ErrorType}.",
                importJob.Id,
                exception.GetType().Name
            );
            return false;
        }
    }
}
