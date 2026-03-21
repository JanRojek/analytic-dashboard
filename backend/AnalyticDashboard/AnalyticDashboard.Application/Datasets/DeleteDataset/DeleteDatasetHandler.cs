using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public sealed class DeleteDatasetHandler
{
    private readonly IDatasetRepository _repository;

    public DeleteDatasetHandler(IDatasetRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(
        DeleteDatasetCommand command, 
        CancellationToken cancellationToken)
    { 
        return await _repository.DeleteAsync(command.Id, cancellationToken);
    }
}