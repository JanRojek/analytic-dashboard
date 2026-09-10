using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Widgets.GetWidgetData;

public abstract record GetWidgetDataResult
{
    public sealed record Success(
        Guid WidgetId,
        WidgetType Type,
        string Title,
        IReadOnlyList<Item> Items
    ) : GetWidgetDataResult;

    public sealed record NotFound : GetWidgetDataResult;

    public sealed record InvalidQuery(
        string Message
    ) : GetWidgetDataResult;

    public sealed record Item(
        string? Label,
        double Value
    );
}
