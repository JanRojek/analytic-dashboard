using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Common.Persistence;
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
        Guid? DatasetId,
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
            ],
            0
        );
    }

    private static async Task<long> ReadResponseRevisionAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return json.GetProperty("revision").GetInt64();
    }

    private async Task<long> ReadDashboardRevisionAsync(Guid dashboardId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Dashboards
            .Where(dashboard => dashboard.Id == dashboardId)
            .Select(dashboard => dashboard.Revision)
            .SingleAsync(CancellationToken);
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
            ],
            0
        );

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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ReadResponseRevisionAsync(response));
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));

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
            ],
            0
        );

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
            ],
            0
        );

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
    public async Task UpdateDashboardWidgets_ShouldSynchronizeWidgetIds_AndRejectDuplicates(
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
            new UpdateDashboardWidgetsRequest(updates, 0),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        if (scenario == "Duplicate")
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertUnchangedAsync(dashboard.DashboardId, before);
            Assert.Equal(0L, await ReadDashboardRevisionAsync(dashboard.DashboardId));
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ReadResponseRevisionAsync(response));
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));

        var saved = await ReadSnapshotsAsync(dashboard.DashboardId);
        Assert.Equal(scenario == "Missing" ? 1 : 2, saved.Count);
        Assert.Equal("First updated", saved[dashboard.FirstWidgetId].Title);
        Assert.False(saved.ContainsKey(dashboard.SecondWidgetId));

        if (scenario == "Unknown")
        {
            Assert.Equal("Second updated", saved[secondWidgetId].Title);
        }
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
            ],
            0
        );

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
            ],
            0
        );

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
            new UpdateDashboardWidgetsRequest([], 0),
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
            new UpdateDashboardWidgetsRequest(null, 0),
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
                ],
                0
            ),
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
                ],
                0
            ),
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
                ],
                0
            ),
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
    public async Task UpdateDashboardWidgets_ShouldReturnOk_WhenDashboardIsEmptyAndUpdateIsEmpty()
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
            new UpdateDashboardWidgetsRequest([], 0),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ReadResponseRevisionAsync(response));
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.Id));

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var widgetCount = await verificationContext.Widgets.CountAsync(
            widget => widget.DashboardId == dashboard.Id,
            CancellationToken
        );

        Assert.Equal(0, widgetCount);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldSynchronizeCreateUpdateDelete_WhenRequestIsValid()
    {
        var userId = Guid.NewGuid();
        var newWidgetId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);

        var updatedType = Enum.GetValues<WidgetType>()[^1];
        var updatedAggregation = Enum.GetValues<AggregationType>()[^1];

        var requestBody = new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "ChangedTitle",
                    new WidgetLayoutRequest(2, 30, 12, 25),
                    type: updatedType,
                    groupByColumn: "Region",
                    measureColumn: "Revenue",
                    aggregation: updatedAggregation
                ),
                CreateUpdate(
                    newWidgetId,
                    dashboard.AlternateDatasetId,
                    "Orders by product 2",
                    new WidgetLayoutRequest(14, 30, 10, 24),
                    type: updatedType,
                    groupByColumn: "Product",
                    measureColumn: "Orders",
                    aggregation: updatedAggregation
                )
            ],
            0
        );

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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ReadResponseRevisionAsync(response));
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));

        var saved = await ReadSnapshotsAsync(dashboard.DashboardId);

        Assert.Equal(2, saved.Count);

        Assert.True(saved.ContainsKey(dashboard.FirstWidgetId));
        Assert.False(saved.ContainsKey(dashboard.SecondWidgetId));
        Assert.True(saved.ContainsKey(newWidgetId));

        var updatedWidget = saved[dashboard.FirstWidgetId];

        Assert.Equal("ChangedTitle", updatedWidget.Title);
        Assert.Equal(dashboard.DatasetId, updatedWidget.DatasetId);
        Assert.Equal(updatedType, updatedWidget.Type);
        Assert.Equal("Region", updatedWidget.GroupByColumn);
        Assert.Equal("Revenue", updatedWidget.MeasureColumn);
        Assert.Equal(updatedAggregation, updatedWidget.Aggregation);
        Assert.Equal(
            (2, 30, 12, 25),
            (updatedWidget.X, updatedWidget.Y, updatedWidget.Width, updatedWidget.Height)
        );

        Assert.Equal(
            before[dashboard.FirstWidgetId].CreatedAtUtc,
            updatedWidget.CreatedAtUtc
        );

        var createdWidget = saved[newWidgetId];

        Assert.Equal(newWidgetId, createdWidget.Id);
        Assert.Equal(dashboard.DashboardId, createdWidget.DashboardId);
        Assert.Equal(dashboard.AlternateDatasetId, createdWidget.DatasetId);
        Assert.Equal("Orders by product 2", createdWidget.Title);
        Assert.Equal(updatedType, createdWidget.Type);
        Assert.Equal("Product", createdWidget.GroupByColumn);
        Assert.Equal("Orders", createdWidget.MeasureColumn);
        Assert.Equal(updatedAggregation, createdWidget.Aggregation);
        Assert.Equal(
            (14, 30, 10, 24),
            (createdWidget.X, createdWidget.Y, createdWidget.Width, createdWidget.Height)
        );
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldReturnRevisionConflict_AndPreserveCommittedState_WhenRevisionIsStale()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);

        using (var firstRequest = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            ValidUpdateRequest(dashboard),
            userId))
        using (var firstResponse = await _fixture.Client.SendAsync(firstRequest, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(1L, await ReadResponseRevisionAsync(firstResponse));
        }

        var committed = await ReadSnapshotsAsync(dashboard.DashboardId);
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));

        var newWidgetId = Guid.NewGuid();
        var staleRequestBody = new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "Stale update must not win",
                    new WidgetLayoutRequest(0, 40, 12, 20)
                ),
                CreateUpdate(
                    newWidgetId,
                    dashboard.DatasetId,
                    "Stale create must not happen",
                    new WidgetLayoutRequest(12, 40, 12, 20)
                )
            ],
            0
        );

        using var staleRequest = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            staleRequestBody,
            userId
        );
        using var staleResponse = await _fixture.Client.SendAsync(staleRequest, CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var problem = await staleResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(0L, problem.GetProperty("expectedRevision").GetInt64());
        Assert.Equal(1L, problem.GetProperty("currentRevision").GetInt64());

        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));
        await AssertUnchangedAsync(dashboard.DashboardId, committed);
        Assert.False((await ReadSnapshotsAsync(dashboard.DashboardId)).ContainsKey(newWidgetId));
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldRejectWholeMixedRequest_WhenNewWidgetHasUnknownDataset()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);
        var newWidgetId = Guid.NewGuid();

        var requestBody = new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "Should not update",
                    new WidgetLayoutRequest(0, 30, 12, 20)
                ),
                CreateUpdate(
                    newWidgetId,
                    Guid.NewGuid(),
                    "Should not be created",
                    new WidgetLayoutRequest(12, 30, 12, 20)
                )
            ],
            0
        );

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await ReadDashboardRevisionAsync(dashboard.DashboardId));
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Fact]
    public async Task UpdateDashboardWidgets_ShouldAcceptExactlyOneOfTwoCompetingRequests_WhenRevisionIsSame()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);

        var firstBody = new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "Winner A first",
                    new WidgetLayoutRequest(0, 30, 12, 20)
                ),
                CreateUpdate(
                    dashboard.SecondWidgetId,
                    dashboard.DatasetId,
                    "Winner A second",
                    new WidgetLayoutRequest(12, 30, 12, 20)
                )
            ],
            0
        );

        var secondBody = new UpdateDashboardWidgetsRequest(
            [
                CreateUpdate(
                    dashboard.FirstWidgetId,
                    dashboard.DatasetId,
                    "Winner B first",
                    new WidgetLayoutRequest(0, 40, 12, 20)
                ),
                CreateUpdate(
                    dashboard.SecondWidgetId,
                    dashboard.DatasetId,
                    "Winner B second",
                    new WidgetLayoutRequest(12, 40, 12, 20)
                )
            ],
            0
        );

        using var firstRequest = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            firstBody,
            userId
        );
        using var secondRequest = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            secondBody,
            userId
        );

        var sendFirst = _fixture.Client.SendAsync(firstRequest, CancellationToken);
        var sendSecond = _fixture.Client.SendAsync(secondRequest, CancellationToken);
        var responses = await Task.WhenAll(sendFirst, sendSecond);

        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));

        var saved = await ReadSnapshotsAsync(dashboard.DashboardId);
        Assert.Equal(2, saved.Count);

        var firstWon = firstResponse.StatusCode == HttpStatusCode.OK;

        Assert.Equal(
            firstWon ? "Winner A first" : "Winner B first",
            saved[dashboard.FirstWidgetId].Title
        );
        Assert.Equal(
            firstWon ? "Winner A second" : "Winner B second",
            saved[dashboard.SecondWidgetId].Title
        );
        Assert.Equal(firstWon ? 30 : 40, saved[dashboard.FirstWidgetId].Y);
        Assert.Equal(firstWon ? 30 : 40, saved[dashboard.SecondWidgetId].Y);
    }

    [Fact]
    public async Task UnitOfWork_ShouldRollbackStagedCreateAndDelete_WhenRevisionChangesBeforeSave()
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);
        var newWidgetId = Guid.NewGuid();

        await using var winningScope = _fixture.Services.CreateAsyncScope();
        await using var losingScope = _fixture.Services.CreateAsyncScope();

        var winningDb = winningScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var losingDb = losingScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var winningDashboard = await winningDb.Dashboards
            .SingleAsync(item => item.Id == dashboard.DashboardId, CancellationToken);
        var losingDashboard = await losingDb.Dashboards
            .SingleAsync(item => item.Id == dashboard.DashboardId, CancellationToken);
        var widgetToDelete = await losingDb.Widgets
            .SingleAsync(widget => widget.Id == dashboard.SecondWidgetId, CancellationToken);

        losingDb.Widgets.Remove(widgetToDelete);
        losingDb.Widgets.Add(new Widget(
            dashboard.DashboardId,
            dashboard.DatasetId,
            Enum.GetValues<WidgetType>()[0],
            "Must be rolled back",
            "Country",
            "Amount",
            Enum.GetValues<AggregationType>()[0],
            new WidgetLayout(12, 0, 12, 20),
            newWidgetId
        ));
        losingDashboard.AdvanceRevision();

        winningDashboard.AdvanceRevision();
        await winningDb.SaveChangesAsync(CancellationToken);

        var unitOfWork = (IUnitOfWork)losingDb;
        var exception = await Assert.ThrowsAsync<OptimisticConcurrencyException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken));
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);

        Assert.Equal(1L, await ReadDashboardRevisionAsync(dashboard.DashboardId));
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Null")]
    [InlineData("Negative")]
    public async Task UpdateDashboardWidgets_ShouldRejectMissingNullOrNegativeExpectedRevision(string scenario)
    {
        var userId = Guid.NewGuid();
        var dashboard = await SeedDashboardAsync(userId);
        var before = await ReadSnapshotsAsync(dashboard.DashboardId);
        var widgets = ValidUpdateRequest(dashboard).Widgets;

        object requestBody = scenario switch
        {
            "Missing" => new { widgets },
            "Null" => new { widgets, expectedRevision = (long?)null },
            "Negative" => new { widgets, expectedRevision = -1L },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            requestBody,
            userId
        );
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await ReadDashboardRevisionAsync(dashboard.DashboardId));
        await AssertUnchangedAsync(dashboard.DashboardId, before);
    }
}
