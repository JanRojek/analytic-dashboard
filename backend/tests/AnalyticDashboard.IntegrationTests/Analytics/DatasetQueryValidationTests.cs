using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Analytics;

public sealed class DatasetQueryValidationTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DatasetQueryValidationTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("Median")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("5")]
    [InlineData("2147483648")]
    public async Task QueryDataset_ShouldRejectInvalidAggregation_BeforeDatasetLookup(string? aggregation)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{Guid.NewGuid()}/datasets/{Guid.NewGuid()}/query");

        request.Content = JsonContent.Create(new QueryDatasetRequest(
            "Country",
            "Amount",
            aggregation!
        ));

        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal(
            "Aggregation must be one of: Sum, Average, Min, Max, Count.",
            Assert.Single(problem.Errors["Aggregation"])
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Executor_ShouldRejectUndefinedAggregation_BeforeDatasetLookup(int aggregation)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<DatasetQueryExecutor>();

        var result = await executor.ExecuteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Country",
            "Amount",
            (AggregationType)aggregation,
            CancellationToken
        );

        var invalid = Assert.IsType<DatasetQueryExecutionResult.InvalidQuery>(result);
        Assert.Equal("Unsupported aggregation type.", invalid.Message);
    }

    [Fact]
    public async Task GetWidgetData_ShouldRejectUndefinedStoredAggregation_BeforeReadingDataset()
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Invalid widget aggregation project");
        var dashboard = new Dashboard(project.Id, "Dashboard");
        var dataset = new Dataset(project.Id, "No ready data");
        var widget = new Widget(
            dashboard.Id,
            dataset.Id,
            WidgetType.Kpi,
            "Invalid stored aggregation",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(
                0,
                0,
                12,
                20
            )
        );
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.AddRange(project, dashboard, dataset, widget);
        dbContext.Entry(widget).Property(entity => entity.Aggregation).CurrentValue = (AggregationType)999;
        await dbContext.SaveChangesAsync(CancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{project.Id}/dashboards/{dashboard.Id}/widgets/{widget.Id}/data"
        );
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid widget query", problem.Title);
        Assert.Equal("Unsupported aggregation type.", problem.Detail);
    }
}
