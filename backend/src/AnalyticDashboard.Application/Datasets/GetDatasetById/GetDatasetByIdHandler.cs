using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Datasets.GetDatasetById;

public sealed class GetDatasetByIdHandler
{
    private readonly IDatasetRepository _repository;

    public GetDatasetByIdHandler(IDatasetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetDatasetByIdResponse?> Handle(
        GetDatasetByIdQuery query, 
        CancellationToken cancellationToken)
    {
        var dataset = await _repository.GetByIdAsync(query.Id, cancellationToken);

        return dataset == null ? null : new GetDatasetByIdResponse(
            dataset.Id,
            dataset.Name,
            dataset.OriginalFileName,
            dataset.CreatedAtUtc,
            dataset.RowCount,
            dataset.ColumnCount
        );
    }
}