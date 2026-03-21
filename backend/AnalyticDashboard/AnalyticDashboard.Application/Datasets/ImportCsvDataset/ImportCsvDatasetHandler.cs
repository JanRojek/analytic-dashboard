using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public sealed class ImportCsvDatasetHandler
{
    private readonly IDatasetRepository _repository;

    public ImportCsvDatasetHandler(IDatasetRepository repository)
    {
        _repository = repository;
    }

    public async Task<ImportCsvDatasetResponse> Handle(
        ImportCsvDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var dataset = new Dataset(
            command.Id,
            command.Name,
            command.OriginalFileName,
            command.StoredPath,
            DateTime.UtcNow,
            command.RowCount,
            command.ColumnCount
        );

        await _repository.AddAsync(dataset, cancellationToken);

        return new ImportCsvDatasetResponse(dataset.Id);
    }
}