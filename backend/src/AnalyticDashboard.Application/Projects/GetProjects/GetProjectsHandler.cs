using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed class GetProjectsHandler
{
    private readonly IProjectRepository _projectRepository;

    public GetProjectsHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<GetProjectsResult> HandleAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > GetProjectsQuery.MaxPageSize)
        {
            return new GetProjectsResult.InvalidPageSize(
                $"Page size must be between 1 and {GetProjectsQuery.MaxPageSize}."
            );
        }

        var normalizedPage = query.Page <= 0
            ? 1
            : query.Page;

        var totalCount = await _projectRepository.CountByOwnerAsync(
            query.OwnerId,
            cancellationToken
        );

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                totalCount / (double)query.PageSize
            )
        );

        var actualPage = Math.Min(
            normalizedPage,
            totalPages
        );

        var skip = (actualPage - 1) * query.PageSize;

        var projects = await _projectRepository.GetPageByOwnerAsync(
            query.OwnerId,
            skip,
            query.PageSize,
            cancellationToken
        );

        var items = projects
            .Select(project => new GetProjectsItem(
                project.Id,
                project.Name,
                project.CreatedAtUtc
            ))
            .ToList();

        return new GetProjectsResult.Success(
            items,
            actualPage,
            query.PageSize,
            totalCount,
            totalPages
        );
    }
}
