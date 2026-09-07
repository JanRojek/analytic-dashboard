using AnalyticDashboard.Application.Profiling;

namespace AnalyticDashboard.Application.Datasets.GetDatasetProfile;

public abstract record GetDatasetProfileResult
{
    public sealed record Found(
        DatasetProfile Profile
    ) : GetDatasetProfileResult;

    public sealed record NotFound
        : GetDatasetProfileResult;
}
