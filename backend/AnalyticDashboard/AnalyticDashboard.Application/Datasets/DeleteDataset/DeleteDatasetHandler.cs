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
        var dataset = await _repository.GetByIdAsync(command.Id, cancellationToken);

        if (dataset is null)
        {
            return false;
        }

        var datasetPath = dataset.StoredPath;

        if (File.Exists(datasetPath))
        {
            File.Delete(datasetPath);
        }
        
        return await _repository.DeleteAsync(command.Id, cancellationToken);
    }
}