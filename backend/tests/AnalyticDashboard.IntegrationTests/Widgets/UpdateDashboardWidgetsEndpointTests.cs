using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Widgets;

public sealed class UpdateDashboardWidgetsEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    private sealed record SeededDashboard(
        Guid ProjectId,
        Guid DashboardId,
        Guid DatasetId,
        Guid AlternateDatasetId,
        Guid FirstWidgetId,
        Guid SecondWidgetId
    );

    private sealed record WidgetSnapshot(
        Guid Id,
        Guid DashboardId,
        Guid DatasetId,
        WidgetType Type,
        string Title,
        string GroupByColumn,
        string MeasureColumn,
        AggregationType Aggregation,
        int X,
        int Y,
        int Width,
        int Height,
        DateTime CreatedAtUtc
    );

    public UpdateDashboardWidgetsEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePutRequest<T>(
        Guid projectId,
        Guid dashboardId,
        T body,
        Guid userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{projectId}/dashboards/{dashboardId}/widgets"
        )
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            userId.ToString()
        );

        return request;
    }

    private static DashboardWidgetUpdateRequest CreateUpdate(
        Guid widgetId,
        Guid datasetId,
        string? title,
        WidgetLayoutRequest? layout,
        WidgetType? type = null,
        string? groupByColumn = "Country",
        string? measureColumn = "Amount",
        AggregationType? aggregation = null)
    {
        return new DashboardWidgetUpdateRequest(
            widgetId,
            datasetId,
            type ?? Enum.GetValues<WidgetType>()[0],
            title,
            groupByColumn,
            measureColumn,
            (aggregation ?? Enum.GetValues<AggregationType>()[0]).ToString(),
            layout
        );
    }

    private static UpdateDashboardWidgetsRequest ValidUpdateRequest(
        SeededDashboard dashboard)
    {
        return new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.DatasetId,
                "First updated",
                new WidgetLayoutRequest(0, 30, 12, 20)
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                dashboard.DatasetId,
                "Second updated",
                new WidgetLayoutRequest(12, 30, 12, 20)
            )
        ]);
    }

    private async Task<SeededDashboard> SeedDashboardAsync(Guid ownerId)
    {
        var project = new Project(ownerId, "Widget update test project");
        var dashboard = new Dashboard(project.Id, "Widget update test dashboard");
        var dataset = new Dataset(project.Id, "Original dataset");
        var alternateDataset = new Dataset(project.Id, "Alternate dataset");

        var originalType = Enum.GetValues<WidgetType>()[0];
        var originalAggregation = Enum.GetValues<AggregationType>()[0];

        var firstWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            originalType,
            "First widget",
            "Country",
            "Amount",
            originalAggregation,
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            originalType,
            "Second widget",
            "Country",
            "Amount",
            originalAggregation,
            new WidgetLayout(12, 0, 12, 20)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.AddRange(
            project,
            dashboard,
            dataset,
            alternateDataset,
            firstWidget,
            secondWidget
        );

        await dbContext.SaveChangesAsync(CancellationToken);

        return new SeededDashboard(
            project.Id,
            dashboard.Id,
            dataset.Id,
            alternateDataset.Id,
            firstWidget.Id,
            secondWidget.Id
        );
    }

    private async Task<Dictionary<Guid, WidgetSnapshot>> ReadSnapshotsAsync(
        Guid dashboardId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var widgets = await dbContext.Widgets
            .AsNoTracking()
            .Where(widget => widget.DashboardId == dashboardId)
            .ToListAsync(CancellationToken);

        return widgets.ToDictionary(
            widget => widget.Id,
            widget => new WidgetSnapshot(
                widget.Id,
                widget.DashboardId,
                widget.DatasetId,
                widget.Type,
                widget.Title,
                widget.GroupByColumn,
                widget.MeasureColumn,
                widget.Aggregation,
                widget.Layout.X,
                widget.Layout.Y,
                widget.Layout.Width,
                widget.Layout.Height,
                widget.CreatedAtUtc
            )
        );
    }

    private async Task AssertUnchangedAsync(
        Guid dashboardId,
        IReadOnlyDictionary<Guid, WidgetSnapshot> before)
    {
        var after = await ReadSnapshotsAsync(dashboardId);
        Assert.Equal(before.Count, after.Count);

        foreach (var (widgetId, expected) in before)
        {
            Assert.True(after.TryGetValue(widgetId, out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldPersistConfigurationAndLayout_WhenRequestIsValid()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var updatedType = Enum.GetValues<WidgetType>()[^1];
        var updatedAggregation = Enum.GetValues<AggregationType>()[^1];

        var requestBody = new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.AlternateDatasetId,
                "  Revenue by region  ",
                new WidgetLayoutRequest(0, 30, 14, 25),
                type: updatedType,
                groupByColumn: "Region",
                measureColumn: "Revenue",
                aggregation: updatedAggregation
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                dashboard.AlternateDatasetId,
                "Orders by product",
                new WidgetLayoutRequest(14, 30, 10, 25),
                type: updatedType,
                groupByColumn: "Product",
                measureColumn: "Orders",
                aggregation: updatedAggregation
            )
        ]);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var saved = await ReadSnapshotsAsync(dashboard.DashboardId);
        Assert.Equal(2, saved.Count);

        var first = saved[dashboard.FirstWidgetId];
        Assert.Equal(dashboard.AlternateDatasetId, first.DatasetId);
        Assert.Equal(updatedType, first.Type);
        Assert.Equal("Revenue by region", first.Title);
        Assert.Equal("Region", first.GroupByColumn);
        Assert.Equal("Revenue", first.MeasureColumn);
        Assert.Equal(updatedAggregation, first.Aggregation);
        Assert.Equal((0, 30, 14, 25), (first.X, first.Y, first.Width, first.Height));
        Assert.Equal(before[dashboard.FirstWidgetId].CreatedAtUtc, first.CreatedAtUtc);
        Assert.Equal(before[dashboard.FirstWidgetId].DashboardId, first.DashboardId);

        var second = saved[dashboard.SecondWidgetId];
        Assert.Equal(dashboard.AlternateDatasetId, second.DatasetId);
        Assert.Equal(updatedType, second.Type);
        Assert.Equal("Orders by product", second.Title);
        Assert.Equal("Product", second.GroupByColumn);
        Assert.Equal("Orders", second.MeasureColumn);
        Assert.Equal(updatedAggregation, second.Aggregation);
        Assert.Equal((14, 30, 10, 25), (second.X, second.Y, second.Width, second.Height));
        Assert.Equal(before[dashboard.SecondWidgetId].CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(before[dashboard.SecondWidgetId].DashboardId, second.DashboardId);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenOneConfigurationIsInvalid()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        // Nonempty but too long: passes the API's basic checks and fails
        // the domain validation inside UpdateDashboardWidgetsHandler.
        var invalidTitle = new string('X', Widget.MaxTitleLength + 1);
        var requestBody = new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.AlternateDatasetId,
                "This change must not be saved",
                new WidgetLayoutRequest(0, 30, 12, 20)
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                dashboard.AlternateDatasetId,
                invalidTitle,
                new WidgetLayoutRequest(12, 30, 12, 20)
            )
        ]);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnConflict_AndPreserveAllWidgets_WhenLayoutsOverlap()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var requestBody = new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.AlternateDatasetId,
                "First changed",
                new WidgetLayoutRequest(0, 30, 12, 20)
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                dashboard.AlternateDatasetId,
                "Second changed",
                new WidgetLayoutRequest(6, 30, 12, 20)
            )
        ]);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Unknown")]
    [InlineData("Duplicate")]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenIdsDoNotMatch(
        string scenario)
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var first = CreateUpdate(
            dashboard.FirstWidgetId,
            dashboard.DatasetId,
            "First updated",
            new WidgetLayoutRequest(0, 30, 12, 20)
        );

        var secondWidgetId = scenario switch
        {
            "Unknown" => Guid.NewGuid(),
            "Duplicate" => dashboard.FirstWidgetId,
            _ => dashboard.SecondWidgetId
        };

        DashboardWidgetUpdateRequest[] updates = scenario switch
        {
            "Missing" => [first],
            "Unknown" or "Duplicate" =>
            [
                first,
                CreateUpdate(
                    secondWidgetId,
                    dashboard.DatasetId,
                    "Second updated",
                    new WidgetLayoutRequest(12, 30, 12, 20)
                )
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardWidgetsRequest(updates),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenDatasetDoesNotExist()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var requestBody = new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.DatasetId,
                "First changed",
                new WidgetLayoutRequest(0, 30, 12, 20)
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                Guid.NewGuid(),
                "Second changed",
                new WidgetLayoutRequest(12, 30, 12, 20)
            )
        ]);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnNotFound_AndPreserveAllWidgets_WhenDashboardBelongsToAnotherUser()
    {
        var dashboardOwnerId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(dashboardOwnerId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            ValidUpdateRequest(dashboard),
            anotherUserId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenLayoutIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var requestBody = new UpdateDashboardWidgetsRequest(
        [
            CreateUpdate(
                dashboard.FirstWidgetId,
                dashboard.DatasetId,
                "First changed",
                null
            ),
            CreateUpdate(
                dashboard.SecondWidgetId,
                dashboard.DatasetId,
                "Second changed",
                new WidgetLayoutRequest(12, 30, 12, 20)
            )
        ]);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnNotFound_WhenDashboardDoesNotExist()
    {
        var userId = Guid.NewGuid();

        using var request = CreatePutRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateDashboardWidgetsRequest([]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenWidgetCollectionIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardWidgetsRequest(null),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenUpdateIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardWidgetsRequest(
            [
                null,
                CreateUpdate(
                    dashboard.SecondWidgetId,
                    dashboard.DatasetId,
                    "Second changed",
                    new WidgetLayoutRequest(12, 30, 12, 20)
                )
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Theory]
    [InlineData(-1, 30, 12, 20)]
    [InlineData(0, -1, 12, 20)]
    [InlineData(0, 30, 5, 20)]
    [InlineData(0, 30, 12, 13)]
    [InlineData(13, 30, 12, 20)]
    public async Task UpdateDashboardWidgets_ShouldReturnBadRequest_AndPreserveAllWidgets_WhenGeometryIsInvalid(
        int x,
        int y,
        int width,
        int height)
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "First changed",
                    new WidgetLayoutRequest(x, y, width, height)
                ),
                CreateUpdate(
                    dashboard.SecondWidgetId,
                    dashboard.DatasetId,
                    "Second changed",
                    new WidgetLayoutRequest(12, 30, 12, 20)
                )
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnConflict_AndPreserveAllWidgets_WhenCoordinatesApproachIntMaxValue()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "First changed",
                    new WidgetLayoutRequest(0, int.MaxValue - 10, 12, 20)
                ),
                CreateUpdate(
                    dashboard.SecondWidgetId,
                    dashboard.DatasetId,
                    "Second changed",
                    new WidgetLayoutRequest(6, int.MaxValue - 5, 12, 20)
                )
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnNoContent_WhenDashboardIsEmptyAndUpdateIsEmpty()
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Empty dashboard project");
        var dashboard = new Dashboard(project.Id, "Empty dashboard");

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.AddRange(project, dashboard);
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        using var request = CreatePutRequest(
            project.Id,
            dashboard.Id,
            new UpdateDashboardWidgetsRequest([]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var widgetCount = await verificationContext.Widgets.CountAsync(
            widget => widget.DashboardId == dashboard.Id,
            CancellationToken
        );

        Assert.Equal(0, widgetCount);
    }
}
