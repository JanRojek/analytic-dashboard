using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IImportJobRepository
{
    Task<ImportJobWorkItem?> ClaimNextPendingAsync(
        CancellationToken cancellationToken
    );

    Task<ImportJobCompletionOutcome> CompleteAsync(
        ImportJobWorkItem workItem,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken
    );

    Task<bool> IsCompletedAsync(
        ImportJobWorkItem workItem,
        string storageKey,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken
    );

    Task<bool> TryFailAsync(
        ImportJobWorkItem workItem,
        string errorMessage,
        CancellationToken cancellationToken
    );

    Task<ImportJob?> GetLatestByDatasetIdAsync(
        Guid datasetId,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<ImportJob>> GetRunningAsync(
        CancellationToken cancellationToken
    );

    Task SaveChangesAsync(
        CancellationToken cancellationToken
    );
}
