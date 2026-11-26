namespace AnalyticDashboard.Application.Datasets.GetDatasetsSummary;

public abstract record GetDatasetsSummaryResult
{
    public sealed record Success(
        int TotalCount,
        int ReadyCount,
        long TotalRows
    ) : GetDatasetsSummaryResult;

    public sealed record NotFound : GetDatasetsSummaryResult;
}
