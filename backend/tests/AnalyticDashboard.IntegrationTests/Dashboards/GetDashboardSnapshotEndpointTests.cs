using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Dashboards;

public sealed class GetDashboardSnapshotEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public GetDashboardSnapshotEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateGetRequest(
        Guid projectId,
        Guid dashboardId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}/dashboards/{dashboardId}/editor-state"
        );

        if (userId.HasValue)
        {
            request.Headers.Add(
                TestAuthHandler.UserIdHeader,
                userId.Value.ToString()
            );
        }

        return request;
    }

    private async Task AddProjectAndDashboardAsync(
        Project project,
        Dashboard dashboard)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);
        dbContext.Dashboards.Add(dashboard);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    private async Task AddDatasetAndWidgetsAsync(
        Dataset dataset,
        params Widget[] widgets)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Datasets.Add(dataset);
        dbContext.Widgets.AddRange(widgets);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    private async Task<GetDashboardSnapshotResponse> ReadSnapshotAsync(
        Guid projectId,
        Guid dashboardId,
        Guid userId)
    {
        using var request = CreateGetRequest(
            projectId,
            dashboardId,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<GetDashboardSnapshotResponse>(
                JsonOptions,
                CancellationToken
            );

        return Assert.IsType<GetDashboardSnapshotResponse>(result);
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnSuccess_WhenDashboardExists()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Dashboard"
        );

        await AddProjectAndDashboardAsync(project, dashboard);

        using var request = CreateGetRequest(
            project.Id,
            dashboard.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<GetDashboardSnapshotResponse>(
                JsonOptions,
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(dashboard.Id, result.DashboardId);
        Assert.Equal(dashboard.Name, result.Name);
        Assert.Equal(dashboard.Revision, result.Revision);
        Assert.Empty(result.Widgets);
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnAllWidgetsWithConfigurationAndLayout()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Project with widgets"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Dashboard with widgets"
        );

        var dataset = new Dataset(
            project.Id,
            "Dataset"
        );

        var firstWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            Enum.GetValues<WidgetType>()[0],
            "First widget",
            "Country",
            "Amount",
            Enum.GetValues<AggregationType>()[0],
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            Enum.GetValues<WidgetType>()[^1],
            "Second widget",
            "Region",
            "Revenue",
            Enum.GetValues<AggregationType>()[^1],
            new WidgetLayout(12, 0, 12, 25)
        );

        secondWidget.DetachDataset();

        await AddProjectAndDashboardAsync(project, dashboard);
        await AddDatasetAndWidgetsAsync(dataset, firstWidget, secondWidget);

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var expectedWidgets = await dbContext.Widgets
            .AsNoTracking()
            .Where(widget => widget.DashboardId == dashboard.Id)
            .OrderBy(widget => widget.CreatedAtUtc)
            .ThenBy(widget => widget.Id)
            .ToArrayAsync(CancellationToken);

        var result = await ReadSnapshotAsync(
            project.Id,
            dashboard.Id,
            TestAuthHandler.User1Id
        );

        Assert.Equal(dashboard.Id, result.DashboardId);
        Assert.Equal(dashboard.Name, result.Name);
        Assert.Equal(dashboard.Revision, result.Revision);
        Assert.Equal(expectedWidgets.Length, result.Widgets.Count);

        Assert.Contains(result.Widgets, widget =>
            widget.Id == firstWidget.Id && widget.DatasetId == dataset.Id);

        Assert.Contains(result.Widgets, widget =>
            widget.Id == secondWidget.Id && widget.DatasetId == null);

        for (var i = 0; i < expectedWidgets.Length; i++)
        {
            var expected = expectedWidgets[i];
            var actual = result.Widgets[i];

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.DatasetId, actual.DatasetId);
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.GroupByColumn, actual.GroupByColumn);
            Assert.Equal(expected.MeasureColumn, actual.MeasureColumn);
            Assert.Equal(expected.Aggregation, actual.Aggregation);
            Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);

            Assert.Equal(expected.Layout.X, actual.Layout.X);
            Assert.Equal(expected.Layout.Y, actual.Layout.Y);
            Assert.Equal(expected.Layout.Width, actual.Layout.Width);
            Assert.Equal(expected.Layout.Height, actual.Layout.Height);
        }
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnNotFound_WhenDashboardDoesNotExist()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Project without requested dashboard"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Existing dashboard"
        );

        await AddProjectAndDashboardAsync(project, dashboard);

        using var request = CreateGetRequest(
            project.Id,
            Guid.NewGuid(),
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 dashboard project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "User2 dashboard"
        );

        await AddProjectAndDashboardAsync(project, dashboard);

        using var request = CreateGetRequest(
            project.Id,
            dashboard.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnNotFound_WhenDashboardBelongsToDifferentProject()
    {
        var project = new Project(
            TestAuthHandler.User1Id,
            "Original project"
        );

        var otherProject = new Project(
            TestAuthHandler.User1Id,
            "Other project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Dashboard"
        );

        await AddProjectAndDashboardAsync(project, dashboard);

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            dbContext.Projects.Add(otherProject);

            await dbContext.SaveChangesAsync(
                CancellationToken
            );
        }

        using var request = CreateGetRequest(
            otherProject.Id,
            dashboard.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetDashboardSnapshot_ShouldReturnUpdatedRevisionAndWidgets_AfterSuccessfulPut()
    {
        var userId = TestAuthHandler.User1Id;

        var project = new Project(
            userId,
            "Snapshot revision project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Revision dashboard"
        );

        var dataset = new Dataset(
            project.Id,
            "Revision dataset"
        );

        var widget = new Widget(
            dashboard.Id,
            dataset.Id,
            Enum.GetValues<WidgetType>()[0],
            "Original widget",
            "Country",
            "Amount",
            Enum.GetValues<AggregationType>()[0],
            new WidgetLayout(0, 0, 12, 20)
        );

        await AddProjectAndDashboardAsync(project, dashboard);
        await AddDatasetAndWidgetsAsync(dataset, widget);

        var before = await ReadSnapshotAsync(
            project.Id,
            dashboard.Id,
            userId
        );

        Assert.Equal(0L, before.Revision);
        Assert.Equal("Original widget", Assert.Single(before.Widgets).Title);

        var update = new UpdateDashboardWidgetsRequest(
            [
                new DashboardWidgetUpdateRequest(
                    widget.Id,
                    dataset.Id,
                    widget.Type,
                    "Updated widget",
                    "Region",
                    "Revenue",
                    widget.Aggregation.ToString(),
                    new WidgetLayoutRequest(0, 30, 12, 20)
                )
            ],
            before.Revision
        );

        using var putRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{project.Id}/dashboards/{dashboard.Id}/widgets"
        );
        putRequest.Content = JsonContent.Create(update);

        putRequest.Headers.Add(
            TestAuthHandler.UserIdHeader,
            userId.ToString()
        );

        using var putResponse = await _fixture.Client.SendAsync(
            putRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            putResponse.StatusCode
        );

        var after = await ReadSnapshotAsync(
            project.Id,
            dashboard.Id,
            userId
        );

        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(dashboard.Id, after.DashboardId);

        var updatedWidget = Assert.Single(after.Widgets);

        Assert.Equal(widget.Id, updatedWidget.Id);
        Assert.Equal(dataset.Id, updatedWidget.DatasetId);
        Assert.Equal("Updated widget", updatedWidget.Title);
        Assert.Equal("Region", updatedWidget.GroupByColumn);
        Assert.Equal("Revenue", updatedWidget.MeasureColumn);

        Assert.Equal(0, updatedWidget.Layout.X);
        Assert.Equal(30, updatedWidget.Layout.Y);
        Assert.Equal(12, updatedWidget.Layout.Width);
        Assert.Equal(20, updatedWidget.Layout.Height);

        Assert.Equal("Original widget", Assert.Single(before.Widgets).Title);
    }

    [Fact]
    public async Task GetEditorSnapshot_ShouldPreserveConsistentState_WhenWriteCommitsBetweenSelects()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationToken
        );

        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var cancellationToken = timeout.Token;
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Concurrent snapshot project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "Concurrent dashboard"
        );

        var dataset = new Dataset(
            project.Id,
            "Concurrent dataset"
        );

        var widget = new Widget(
            dashboard.Id,
            dataset.Id,
            Enum.GetValues<WidgetType>()[0],
            "Original widget",
            "Country",
            "Amount",
            Enum.GetValues<AggregationType>()[0],
            new WidgetLayout(0, 0, 12, 20)
        );

        await AddProjectAndDashboardAsync(project, dashboard);
        await AddDatasetAndWidgetsAsync(dataset, widget);

        await using var seedScope = _fixture.Services.CreateAsyncScope();

        var seedContext = seedScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var connectionString = seedContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Missing test connection string.");

        var barrier = new WidgetReadBarrier();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(barrier)
            .Options;

        await using var readerContext = new AppDbContext(options);

        var repository = new DashboardRepository(readerContext);

        var reading = repository.GetEditorSnapshotAsync(
            dashboard.Id,
            project.Id,
            userId,
            cancellationToken
        );

        try
        {
            await barrier.BeforeWidgetsRead.WaitAsync(cancellationToken);

            await using var competingScope = _fixture.Services.CreateAsyncScope();

            var competingContext = competingScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var competingDashboard = await competingContext.Dashboards
                .SingleAsync(
                    item => item.Id == dashboard.Id,
                    cancellationToken
                );

            var competingWidget = await competingContext.Widgets
                .SingleAsync(
                    item => item.Id == widget.Id,
                    cancellationToken
                );

            competingWidget.Rename("Updated by concurrent writer");
            competingDashboard.AdvanceRevision();

            var newWidget = new Widget(
                dashboard.Id,
                dataset.Id,
                widget.Type,
                "New concurrent widget",
                "Region",
                "Revenue",
                widget.Aggregation,
                new WidgetLayout(12, 0, 12, 20)
            );

            competingContext.Widgets.Add(newWidget);

            await competingContext.SaveChangesAsync(
                cancellationToken
            );
        }
        finally
        {
            barrier.Release();
        }

        var snapshot = await reading.WaitAsync(cancellationToken);

        Assert.NotNull(snapshot);

        Assert.Equal(0L, snapshot.Dashboard.Revision);
        Assert.Equal("Original widget", Assert.Single(snapshot.Widgets).Title);

        var latest = await ReadSnapshotAsync(
            project.Id,
            dashboard.Id,
            userId
        );

        Assert.Equal(1L, latest.Revision);
        Assert.Equal(2, latest.Widgets.Count);

        Assert.Contains(latest.Widgets, item =>
            item.Id == widget.Id
            && item.Title == "Updated by concurrent writer");

        Assert.Contains(latest.Widgets, item =>
            item.Title == "New concurrent widget");
    }

    private sealed class WidgetReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> _beforeWidgetsRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _allowWidgetsRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _paused;

        public Task BeforeWidgetsRead => _beforeWidgetsRead.Task;

        public void Release()
        {
            _allowWidgetsRead.TrySetResult(true);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM \"Widgets\"",
                    StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                _beforeWidgetsRead.TrySetResult(true);

                await _allowWidgetsRead.Task.WaitAsync(
                    cancellationToken
                );
            }

            return await base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken
            );
        }
    }
}
