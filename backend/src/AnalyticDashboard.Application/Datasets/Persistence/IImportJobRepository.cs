using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IImportJobRepository
{
    Task<ImportJobWorkItem?> GetNextPendingAsync(
        CancellationToken cancellationToken
    );

    Task<ImportJob?> GetByIdAsync(
        Guid importJobId,
        CancellationToken cancellationToken
    );

    Task SaveChangesAsync(
        CancellationToken cancellationToken
    );
}
