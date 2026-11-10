using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Widgets;

public sealed class DetachedWidgetEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DetachedWidgetEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetWidgets_ShouldReturnCompleteDetachedWidget()
    {
        var (ownerId, projectId, widget) = await SeedDetachedWidgetAsync();

        using var request = CreateRequest(
            $"/projects/{projectId}/dashboards/{widget.DashboardId}/widgets",
            ownerId
        );
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var item = Assert.Single(items.EnumerateArray());

        Assert.Equal(widget.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(widget.DashboardId, item.GetProperty("dashboardId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("datasetId").ValueKind);
        Assert.Equal(widget.Type.ToString(), item.GetProperty("type").GetString());
        Assert.Equal(widget.Title, item.GetProperty("title").GetString());
        Assert.Equal(widget.GroupByColumn, item.GetProperty("groupByColumn").GetString());
        Assert.Equal(widget.MeasureColumn, item.GetProperty("measureColumn").GetString());
        Assert.Equal(widget.Aggregation.ToString(), item.GetProperty("aggregation").GetString());
        Assert.Equal(widget.CreatedAtUtc, item.GetProperty("createdAtUtc").GetDateTime());

        var layout = item.GetProperty("layout");
        Assert.Equal(widget.Layout.X, layout.GetProperty("x").GetInt32());
        Assert.Equal(widget.Layout.Y, layout.GetProperty("y").GetInt32());
        Assert.Equal(widget.Layout.Width, layout.GetProperty("width").GetInt32());
        Assert.Equal(widget.Layout.Height, layout.GetProperty("height").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetWidgetData_ShouldRespectOwnershipBeforeReturningSourceRemoved(bool foreignOwner)
    {
        var (ownerId, projectId, widget) = await SeedDetachedWidgetAsync();

        string connectionString;
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Database.GetDbConnection().ConnectionString;
        }

        await using var factory = new CustomWebApplicationFactory(connectionString)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddDbContext<AppDbContext>(options =>
                        options.AddInterceptors(new RejectDatasetQueriesInterceptor())
                    )
                )
            );

        using var client = factory.CreateClient();
        using var request = CreateRequest(
            $"/projects/{projectId}/dashboards/{widget.DashboardId}/widgets/{widget.Id}/data",
            foreignOwner ? Guid.NewGuid() : ownerId
        );
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(
            foreignOwner ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
            response.StatusCode
        );

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        if (foreignOwner)
        {
            Assert.False(problem.TryGetProperty("code", out _));
        }
        else
        {
            Assert.Equal(409, problem.GetProperty("status").GetInt32());
            Assert.Equal("widget_source_removed", problem.GetProperty("code").GetString());
        }
    }

    private async Task<(Guid OwnerId, Guid ProjectId, Widget Widget)> SeedDetachedWidgetAsync()
    {
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Detached widget project");
        var dashboard = new Dashboard(project.Id, "Dashboard");
        var dataset = new Dataset(project.Id, "Source dataset");
        var widget = new Widget(
            dashboard.Id,
            dataset.Id,
            WidgetType.BarChart,
            "Saved chart",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(2, 3, 6, 14)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.AddRange(project, dashboard, dataset, widget);
        await context.SaveChangesAsync(CancellationToken);

        widget.DetachDataset();
        await context.SaveChangesAsync(CancellationToken);
        await context.Entry(widget).ReloadAsync(CancellationToken);

        return (ownerId, project.Id, widget);
    }

    private static HttpRequestMessage CreateRequest(string url, Guid ownerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserIdHeader, ownerId.ToString());

        return request;
    }

    private sealed class RejectDatasetQueriesInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"datasets\"", StringComparison.Ordinal)
                || command.CommandText.Contains("\"dataset_versions\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Detached widgets must not access dataset queries.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
