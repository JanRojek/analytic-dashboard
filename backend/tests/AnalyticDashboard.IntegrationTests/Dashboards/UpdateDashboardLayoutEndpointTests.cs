using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Dashboards;

public sealed class UpdateDashboardLayoutEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    private sealed record SeededDashboard(
        Guid ProjectId,
        Guid DashboardId,
        Guid FirstWidgetId,
        Guid SecondWidgetId
    );

    public UpdateDashboardLayoutEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePutRequest<T>(
        Guid projectId,
        Guid dashboardId,
        T body,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{projectId}/dashboards/{dashboardId}/layout"
        )
        {
            Content = JsonContent.Create(body)
        };

        if (userId.HasValue)
        {
            request.Headers.Add(
                TestAuthHandler.UserIdHeader,
                userId.Value.ToString()
            );
        }

        return request;
    }

    private static WidgetPlacementRequest CreatePlacement(
        Guid widgetId,
        int x,
        int y,
        int width = 12,
        int height = 20)
    {
        return new WidgetPlacementRequest(
            widgetId,
            new WidgetLayoutRequest(x, y, width, height)
        );
    }

    private async Task<SeededDashboard> AddDashboardWithWidgetsAsync(Guid userId)
    {
        var project = new Project(
            userId,
            "Layout test project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Layout test dashboard"
        );

        var dataset = new Dataset(
            project.Id,
            "Layout test dataset"
        );

        var widgetType = Enum.GetValues<WidgetType>()
            .First();

        var aggregation = Enum.GetValues<AggregationType>()
            .First();

        var firstWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            widgetType,
            "First widget",
            "Country",
            "Amount",
            aggregation,
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            widgetType,
            "Second widget",
            "Country",
            "Amount",
            aggregation,
            new WidgetLayout(12, 0, 12, 20)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.AddRange(
            project,
            dashboard,
            dataset,
            firstWidget,
            secondWidget
        );

        await dbContext.SaveChangesAsync(CancellationToken);

        return new SeededDashboard(
            project.Id,
            dashboard.Id,
            firstWidget.Id,
            secondWidget.Id
        );
    }

    private async Task AssertLayoutsAsync(
        SeededDashboard dashboard,
        WidgetLayoutRequest expectedFirst,
        WidgetLayoutRequest expectedSecond)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var savedWidgets = await dbContext.Widgets
            .AsNoTracking()
            .Where(widget =>
                widget.Id == dashboard.FirstWidgetId ||
                widget.Id == dashboard.SecondWidgetId)
            .ToListAsync(CancellationToken);

        Assert.Equal(2, savedWidgets.Count);

        var firstLayout = savedWidgets
            .Single(widget => widget.Id == dashboard.FirstWidgetId)
            .Layout;

        var secondLayout = savedWidgets
            .Single(widget => widget.Id == dashboard.SecondWidgetId)
            .Layout;

        AssertLayout(expectedFirst, firstLayout);
        AssertLayout(expectedSecond, secondLayout);
    }

    private static void AssertLayout(
        WidgetLayoutRequest expected,
        WidgetLayout actual)
    {
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
    }

    private Task AssertOriginalLayoutsAsync(SeededDashboard dashboard)
    {
        return AssertLayoutsAsync(
            dashboard,
            new WidgetLayoutRequest(0, 0, 12, 20),
            new WidgetLayoutRequest(12, 0, 12, 20)
        );
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldPersistAllWidgetLayouts_WhenRequestIsValid()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        var firstLayout = new WidgetLayoutRequest(0, 30, 14, 25);
        var secondLayout = new WidgetLayoutRequest(14, 30, 10, 25);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                new WidgetPlacementRequest(dashboard.FirstWidgetId, firstLayout),
                new WidgetPlacementRequest(dashboard.SecondWidgetId, secondLayout)
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await AssertLayoutsAsync(dashboard, firstLayout, secondLayout);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnConflict_AndPreserveLayouts_WhenWidgetsOverlap()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, 30),
                CreatePlacement(dashboard.SecondWidgetId, 6, 30)
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Unknown")]
    [InlineData("Duplicate")]
    public async Task UpdateDashboardLayout_ShouldReturnBadRequest_AndPreserveLayouts_WhenWidgetIdsDoNotMatch(
        string scenario)
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        WidgetPlacementRequest[] placements = scenario switch
        {
            "Missing" =>
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, 30)
            ],
            "Unknown" =>
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, 30),
                CreatePlacement(Guid.NewGuid(), 12, 30)
            ],
            "Duplicate" =>
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, 30),
                CreatePlacement(dashboard.FirstWidgetId, 12, 30)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(placements),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnNotFound_WhenDashboardDoesNotExist()
    {
        var userId = Guid.NewGuid();

        using var request = CreatePutRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateDashboardLayoutRequest([]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnNotFound_AndPreserveLayouts_WhenDashboardBelongsToDifferentUser()
    {
        var dashboardOwnerId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(dashboardOwnerId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, 30),
                CreatePlacement(dashboard.SecondWidgetId, 12, 30)
            ]),
            anotherUserId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnBadRequest_AndPreserveLayouts_WhenWidgetCollectionIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(null),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnBadRequest_AndPreserveLayouts_WhenLayoutIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                new WidgetPlacementRequest(dashboard.FirstWidgetId, null),
                CreatePlacement(dashboard.SecondWidgetId, 12, 30)
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnBadRequest_AndPreserveLayouts_WhenPlacementIsNull()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new
            {
                widgetPlacementRequests = new object?[]
                {
                    null,
                    new
                    {
                        widgetId = dashboard.SecondWidgetId,
                        layoutRequest = new WidgetLayoutRequest(12, 30, 12, 20)
                    }
                }
            },
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Theory]
    [InlineData(-1, 30, 12, 20)]
    [InlineData(0, -1, 12, 20)]
    [InlineData(0, 30, 5, 20)]
    [InlineData(0, 30, 12, 13)]
    [InlineData(13, 30, 12, 20)]
    public async Task UpdateDashboardLayout_ShouldReturnBadRequest_AndPreserveLayouts_WhenGeometryIsInvalid(
        int x,
        int y,
        int width,
        int height)
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                CreatePlacement(dashboard.FirstWidgetId, x, y, width, height),
                CreatePlacement(dashboard.SecondWidgetId, 12, 30)
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnConflict_AndPreserveLayouts_WhenCoordinatesApproachIntMaxValue()
    {
        var userId = Guid.NewGuid();
        var dashboard = await AddDashboardWithWidgetsAsync(userId);

        using var request = CreatePutRequest(
            dashboard.ProjectId,
            dashboard.DashboardId,
            new UpdateDashboardLayoutRequest(
            [
                CreatePlacement(dashboard.FirstWidgetId, 0, int.MaxValue - 10),
                CreatePlacement(dashboard.SecondWidgetId, 6, int.MaxValue - 5)
            ]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await AssertOriginalLayoutsAsync(dashboard);
    }

    [Fact]
    public async Task UpdateDashboardLayout_ShouldReturnNoContent_WhenDashboardIsEmptyAndLayoutIsEmpty()
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Empty dashboard project");
        var dashboard = new Dashboard(project.Id, "Empty dashboard");

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            dbContext.AddRange(project, dashboard);

            await dbContext.SaveChangesAsync(CancellationToken);
        }

        using var request = CreatePutRequest(
            project.Id,
            dashboard.Id,
            new UpdateDashboardLayoutRequest([]),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope = _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var widgetCount = await verificationContext.Widgets
            .CountAsync(widget => widget.DashboardId == dashboard.Id, CancellationToken);

        Assert.Equal(0, widgetCount);
    }
}
