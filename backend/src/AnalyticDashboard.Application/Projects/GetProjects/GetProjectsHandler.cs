using AnalyticDashboard.Application.Common.Pagination;
using AnalyticDashboard.Application.Projects.Persistence;

namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed class GetProjectsHandler
{
    private readonly IProjectRepository _projectRepository;

    public GetProjectsHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<PagedResult<GetProjectsItem>> HandleAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var totalCount = await _projectRepository.CountByOwnerAsync(
            query.OwnerId,
            cancellationToken
        );

        var pagination = Pagination.Calculate(
            query.Pagination,
            totalCount
        );

        var projects = await _projectRepository.GetPageByOwnerAsync(
            query.OwnerId,
            pagination.Skip,
            pagination.PageSize,
            cancellationToken
        );

        var items = projects
            .Select(project => new GetProjectsItem(
                project.Id,
                project.Name,
                project.CreatedAtUtc
            ))
            .ToList();

        return new PagedResult<GetProjectsItem>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount,
            pagination.TotalPages
        );
    }
}
