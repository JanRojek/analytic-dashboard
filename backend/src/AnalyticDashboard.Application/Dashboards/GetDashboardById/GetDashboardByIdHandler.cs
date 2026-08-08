using AnalyticDashboard.Domain.Repositories;

namespace AnalyticDashboard.Application.Dashboards.GetDashboardById;

public class GetDashboardByIdHandler
{
    private IDashboardRepository _repository;
    
    public GetDashboardByIdHandler(IDashboardRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<GetDashboardByIdResponse?> Handle(
        GetDashboardByIdQuery query, 
        CancellationToken cancellationToken)
    {
        var dashboard = await _repository.GetByIdAsync(query.Id, cancellationToken);

        if (dashboard == null)
        {
            return null;
        }

        return new GetDashboardByIdResponse(
            dashboard.Id,
            dashboard.DatasetId,
            dashboard.Dataset!.Name,
            dashboard.Name,
            dashboard.CreatedAtUtc
        );
    }
}