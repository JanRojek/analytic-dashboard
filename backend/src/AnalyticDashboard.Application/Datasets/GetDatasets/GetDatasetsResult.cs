using AnalyticDashboard.Application.Common.Pagination;

namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public abstract record GetDatasetsResult
{
    public sealed record Success(
        PagedResult<GetDatasetsItem> Data
    ) : GetDatasetsResult;

    public sealed record NotFound : GetDatasetsResult;
}
