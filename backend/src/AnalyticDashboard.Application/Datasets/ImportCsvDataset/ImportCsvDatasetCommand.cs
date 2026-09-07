namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public sealed record ImportCsvDatasetCommand(
    Guid ProjectId,
    Guid OwnerId,
    Stream FileStream,
    string FileName
);
