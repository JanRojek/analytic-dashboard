using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Dashboards.CreateDashboard;

public sealed class CreateDashboardHandler
{
    private readonly IDashboardRepository _repository;
    private readonly IDatasetRepository _datasetRepository;

    public CreateDashboardHandler(
        IDashboardRepository repository,
        IDatasetRepository datasetRepository)
    {
        _repository = repository;
        _datasetRepository = datasetRepository;
    }

    public async Task<CreateDashboardResponse?> Handle(
        CreateDashboardCommand command,
        CancellationToken cancellationToken)
    {
        var dataset =
            await _datasetRepository.GetByIdAndProjectOwnerAsync(
                command.DatasetId,
                command.ProjectId,
                command.OwnerId,
                cancellationToken
            );

        if (dataset is null)
        {
            return null;
        }

        var dashboard = new Dashboard(
            Guid.NewGuid(),
            command.DatasetId,
            command.Name,
            DateTime.UtcNow
        );

        await _repository.AddAsync(
            dashboard,
            cancellationToken
        );

        return new CreateDashboardResponse(
            dashboard.Id
        );
    }
}
