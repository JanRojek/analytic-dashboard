using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Widgets;

public sealed class CreateWidgetEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public CreateWidgetEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePostRequest(
        Guid projectId,
        Guid dashboardId,
        CreateWidgetRequest body,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{projectId}/dashboards/{dashboardId}/widgets"
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

    private async Task AddEntitiesAsync(
        Project dashboardProject,
        Project datasetProject,
        Dashboard dashboard,
        Dataset dataset)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.AddRange(
            dashboardProject,
            datasetProject,
            dashboard,
            dataset
        );

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task CreateWidget_ShouldReturnNotFound_WhenDatasetBelongsToDifferentProject()
    {
        var userId = Guid.NewGuid();

        var dashboardProject = new Project(
            userId,
            "Dashboard project"
        );

        var datasetProject = new Project(
            userId,
            "Dataset project"
        );

        var dashboard = new Dashboard(
            dashboardProject.Id,
            "Dashboard"
        );

        var foreignDataset = new Dataset(
            datasetProject.Id,
            "Foreign dataset"
        );

        await AddEntitiesAsync(
            dashboardProject,
            datasetProject,
            dashboard,
            foreignDataset
        );

        var widgetType = Enum.GetValues<WidgetType>()
            .First();

        using var request = CreatePostRequest(
            dashboardProject.Id,
            dashboard.Id,
            new CreateWidgetRequest(
                foreignDataset.Id,
                widgetType,
                "Illegal widget",
                "Country",
                "Amount",
                "Sum"
            ),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var widgetExists =
            await dbContext.Widgets.AnyAsync(
                widget =>
                    widget.DashboardId == dashboard.Id,
                CancellationToken
            );

        Assert.False(
            widgetExists
        );
    }
}
