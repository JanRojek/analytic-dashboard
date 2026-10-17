using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.GetImportStatus;

public abstract record GetImportStatusResult
{
    public sealed record Found(
        Guid ImportJobId,
        Guid DatasetVersionId,
        ImportJobStatus Status,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc
    ) : GetImportStatusResult;

    public sealed record NotFound
        : GetImportStatusResult;
}
