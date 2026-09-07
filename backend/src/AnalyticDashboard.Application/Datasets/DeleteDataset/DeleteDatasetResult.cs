namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public abstract record DeleteDatasetResult
{
    public sealed record Success
        : DeleteDatasetResult;

    public sealed record NotFound
        : DeleteDatasetResult;
}
