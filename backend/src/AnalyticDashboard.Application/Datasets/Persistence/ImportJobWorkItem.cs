using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.Persistence;

public sealed record ImportJobWorkItem(
    ImportJob Job,
    DatasetVersion Version,
    Dataset Dataset
);
