using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;

namespace AnalyticDashboard.Application.Datasets.GetDatasetsSummary;

public sealed class GetDatasetsSummaryHandler
{
    private readonly IProjectRepository _projectRepository;
    private readonly IDatasetRepository _datasetRepository;

    public GetDatasetsSummaryHandler(
        IDatasetRepository datasetRepository,
        IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
        _datasetRepository = datasetRepository;
    }

    public async Task<GetDatasetsSummaryResult> HandleAsync(
        GetDatasetsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (project is null)
        {
            return new GetDatasetsSummaryResult.NotFound();
        }

        var summary = await _datasetRepository.GetSummaryByProjectAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        return new GetDatasetsSummaryResult.Success(
            summary.TotalCount,
            summary.ReadyCount,
            summary.TotalRows
        );
    }
}
