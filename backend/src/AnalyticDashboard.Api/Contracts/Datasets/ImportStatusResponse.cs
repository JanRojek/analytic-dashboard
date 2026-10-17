using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Api.Contracts.Datasets;

public sealed record ImportStatusResponse(
    Guid ImportJobId,
    Guid DatasetVersionId,
    ImportJobStatus Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage
);
