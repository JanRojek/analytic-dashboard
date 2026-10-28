using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Widgets.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.Application.Dashboards.UpdateDashboardLayout;

public sealed class UpdateDashboardLayoutHandler
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IWidgetRepository _widgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDashboardLayoutHandler(
        IDashboardRepository dashboardRepository,
        IWidgetRepository widgetRepository,
        IUnitOfWork unitOfWork)
    {
        _dashboardRepository = dashboardRepository;
        _widgetRepository = widgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<UpdateDashboardLayoutResult> HandleAsync(
        UpdateDashboardLayoutCommand command,
        CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardRepository.GetByIdAndProjectOwnerAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (dashboard is null)
        {
            return new UpdateDashboardLayoutResult.DashboardNotFound();
        }

        var dashboardWidgets = await _widgetRepository.GetAllForUpdateAsync(
            command.DashboardId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        var widgetsById = dashboardWidgets.ToDictionary(widget => widget.Id);

        if (!HaveSameWidgetIds(widgetsById, command.WidgetLayoutChanges))
        {
            return new UpdateDashboardLayoutResult.InvalidWidgetSet(
                "Provided widget IDs do not match the dashboard widgets."
            );
        }

        for (var i = 0; i < command.WidgetLayoutChanges.Count - 1; i++)
        {
            for (var j = i + 1; j < command.WidgetLayoutChanges.Count; j++)
            {
                var firstChange = command.WidgetLayoutChanges[i];
                var secondChange = command.WidgetLayoutChanges[j];

                if (Intersects(firstChange.NewLayout, secondChange.NewLayout))
                {
                    return new UpdateDashboardLayoutResult.LayoutCollision();
                }
            }
        }

        foreach (var change in command.WidgetLayoutChanges)
        {
            var widget = widgetsById[change.WidgetId];

            widget.UpdateLayout(change.NewLayout);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateDashboardLayoutResult.Success();
    }

    private static bool HaveSameWidgetIds(
        Dictionary<Guid, Widget> widgetsById,
        IReadOnlyList<WidgetLayoutChange> changes)
    {
        if (widgetsById.Count != changes.Count)
            return false;

        var seenIds = new HashSet<Guid>();

        foreach (var change in changes)
        {
            if (!widgetsById.ContainsKey(change.WidgetId) ||
                !seenIds.Add(change.WidgetId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Intersects(
        WidgetLayout first,
        WidgetLayout second)
    {
        var firstRight = (long)first.X + first.Width;
        var secondRight = (long)second.X + second.Width;

        var firstBottom = (long)first.Y + first.Height;
        var secondBottom = (long)second.Y + second.Height;

        return first.X < secondRight &&
               second.X < firstRight &&
               first.Y < secondBottom &&
               second.Y < firstBottom;
    }
}
