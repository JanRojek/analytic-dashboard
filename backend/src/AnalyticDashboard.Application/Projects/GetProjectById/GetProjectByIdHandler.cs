using AnalyticDashboard.Application.Projects.Persistence;

namespace AnalyticDashboard.Application.Projects.GetProjectById;

public sealed class GetProjectByIdHandler
{
    private readonly IProjectRepository _projectRepository;

    public GetProjectByIdHandler(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<GetProjectByIdResult> HandleAsync(
        GetProjectByIdQuery query,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            query.ProjectId,
            query.OwnerId,
            cancellationToken
        );

        if (project == null)
        {
            return new GetProjectByIdResult.NotFound();
        }

        return new GetProjectByIdResult.Found(
            project.Id,
            project.Name,
            project.CreatedAtUtc
        );
    }
}
