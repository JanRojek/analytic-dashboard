using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.UnitTests.Widgets;

public sealed class WidgetTests
{
    [Fact]
    public void UpdateConfiguration_ShouldNotModifyWidget_WhenGroupByColumnIsInvalid()
    {
        var dashboardId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();

        var widget = new Widget(
            dashboardId,
            datasetId,
            WidgetType.BarChart,
            "Widget",
            "Country",
            "Amount",
            AggregationType.Average,
            new WidgetLayout(0, 0, 6, 14)
        );

        var originalDatasetId = widget.DatasetId;
        var originalType = widget.Type;
        var originalTitle = widget.Title;
        var originalGroupByColumn = widget.GroupByColumn;
        var originalMeasureColumn = widget.MeasureColumn;
        var originalAggregation = widget.Aggregation;

        Assert.Throws<ArgumentException>(() =>
            widget.UpdateConfiguration(
                Guid.NewGuid(),
                WidgetType.LineChart,
                "New title",
                "",
                "Revenue",
                AggregationType.Sum
            )
        );

        Assert.Equal(originalDatasetId, widget.DatasetId);
        Assert.Equal(originalType, widget.Type);
        Assert.Equal(originalTitle, widget.Title);
        Assert.Equal(originalGroupByColumn, widget.GroupByColumn);
        Assert.Equal(originalMeasureColumn, widget.MeasureColumn);
        Assert.Equal(originalAggregation, widget.Aggregation);
    }
}
