using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;

namespace AnalyticDashboard.UnitTests.Widgets;

public sealed class WidgetTests
{
    [Fact]
    public void Constructor_ShouldRejectEmptyDatasetId()
    {
        Assert.Throws<ArgumentException>(() => new Widget(
            Guid.NewGuid(),
            Guid.Empty,
            WidgetType.BarChart,
            "Widget",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 6, 14)
        ));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DetachDataset_ShouldPreserveAllOtherFields(int detachCount)
    {
        var datasetId = Guid.NewGuid();
        var widget = new Widget(
            Guid.NewGuid(),
            datasetId,
            WidgetType.BarChart,
            "Widget",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 6, 14)
        );

        Assert.Equal(datasetId, widget.DatasetId);

        var before = (
            widget.Id,
            widget.DashboardId,
            widget.Type,
            widget.Title,
            widget.Layout,
            widget.GroupByColumn,
            widget.MeasureColumn,
            widget.Aggregation,
            widget.CreatedAtUtc
        );

        for (var i = 0; i < detachCount; i++)
        {
            widget.DetachDataset();
        }

        Assert.Null(widget.DatasetId);
        Assert.Equal(
            before,
            (
                widget.Id,
                widget.DashboardId,
                widget.Type,
                widget.Title,
                widget.Layout,
                widget.GroupByColumn,
                widget.MeasureColumn,
                widget.Aggregation,
                widget.CreatedAtUtc
            )
        );
    }

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
