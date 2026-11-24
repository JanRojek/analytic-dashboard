using AnalyticDashboard.Application.Common.Pagination;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;

namespace AnalyticDashboard.Application.Dashboards.GetDashboards;

public sealed class GetDashboardsHandler
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IProjectRepository _projectRepository;

    public GetDashboardsHandler(
        IDashboardRepository dashboardRepository,
        IProjectRepository projectRepository)
    {
        _dashboardRepository = dashboardRepository;
        _projectRepository = projectRepository;
    }

    public async Task<GetDashboardsResult> HandleAsync(
        GetDashboardsQuery query,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (project is null)
        {
            return new GetDashboardsResult.NotFound();
        }

        var totalCount = await _dashboardRepository.CountByProjectAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        var pagination = Pagination.Calculate(
            query.Pagination,
            totalCount
        );

        var dashboards = await _dashboardRepository.GetPageByProjectAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            pagination.Skip,
            pagination.PageSize,
            cancellationToken
        );

        var items = dashboards
            .Select(dashboard => new GetDashboardsItem(
                dashboard.Id,
                dashboard.ProjectId,
                dashboard.Name,
                dashboard.CreatedAtUtc
            ))
            .ToList();

        return new GetDashboardsResult.Success(
            new PagedResult<GetDashboardsItem>(
                items,
                pagination.Page,
                pagination.PageSize,
                totalCount,
                pagination.TotalPages
            )
        );
    }
}
