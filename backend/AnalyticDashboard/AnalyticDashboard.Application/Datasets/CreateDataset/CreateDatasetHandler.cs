using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Datasets.CreateDataset;

public sealed class CreateDatasetHandler
{
    private readonly IDatasetRepository _repository;

    public CreateDatasetHandler(IDatasetRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateDatasetResponse> Handle(
        CreateDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var dataset = new Dataset(
            Guid.NewGuid(),
            command.Name,
            command.OriginalFileName,
            command.StoredPath,
            DateTime.UtcNow,
            command.RowCount,
            command.ColumnCount
        );

        await _repository.AddAsync(dataset, cancellationToken);

        return new CreateDatasetResponse(dataset.Id);
    }
}