using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public interface IWidgetDataReader
{
    Task<GetWidgetDataResponse> ReadDataAsync(
        Widget widget,
        string datasetPath,
        CancellationToken cancellationToken);
}