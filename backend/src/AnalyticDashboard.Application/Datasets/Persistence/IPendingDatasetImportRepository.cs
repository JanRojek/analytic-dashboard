using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public interface IPendingDatasetImportRepository
{
    Task CreateAsync(
        Dataset dataset,
        DatasetVersion version,
        ImportJob importJob,
        CancellationToken cancellationToken
    );
}
