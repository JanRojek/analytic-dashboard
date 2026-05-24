using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Application.Profiling;

namespace AnalyticDashboard.Application.Datasets.GetDatasetProfile;

public sealed class GetDatasetProfileHandler
{
    private readonly IDatasetRepository _repository;
    private readonly IDatasetProfileReader _profileReader;
    
    public GetDatasetProfileHandler(
        IDatasetRepository repository,
        IDatasetProfileReader profileReader)
    {
        _repository = repository;
        _profileReader = profileReader;
    }

    public async Task<GetDatasetProfileResponse?> Handle(
        GetDatasetProfileQuery query,
        CancellationToken cancellationToken)
    {
        var dataset = await _repository.GetByIdAsync(query.DatasetId, cancellationToken);

        if (dataset is null)
        {
            return null;
        }

        if (!File.Exists(dataset.StoredPath))
        {
            throw new FileNotFoundException("Dataset file not found.", dataset.StoredPath);
        }

        return await _profileReader.ReadProfileAsync(
            dataset.Id,
            dataset.Name,
            dataset.OriginalFileName,
            dataset.StoredPath,
            dataset.RowCount,
            dataset.ColumnCount,
            cancellationToken);
    }
}