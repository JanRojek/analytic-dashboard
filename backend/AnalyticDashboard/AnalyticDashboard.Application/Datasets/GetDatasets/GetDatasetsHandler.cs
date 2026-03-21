using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public sealed class GetDatasetsHandler
{
    private readonly IDatasetRepository _repository;

    public GetDatasetsHandler(IDatasetRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GetDatasetsResponse>> Handle(
        GetDatasetsQuery query, 
        CancellationToken cancellationToken)
    {
        var datasets = await _repository.GetAllAsync(cancellationToken);

        return datasets.Select(d => new GetDatasetsResponse(
            d.Id,
            d.Name,
            d.OriginalFileName,
            d.CreatedAtUtc,
            d.RowCount,
            d.ColumnCount
        )).ToList();
    }
}