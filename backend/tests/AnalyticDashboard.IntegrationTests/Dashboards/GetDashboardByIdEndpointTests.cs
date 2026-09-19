using System.Net;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Dashboards;

public sealed class GetDashboardByIdEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetDashboardByIdEndpointTests(ApiFixture fixture)
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
            $"/projects/{projectId}/dashboards/{dashboardId}"
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

    [Fact]
    public async Task GetDashboardById_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 dashboard project"
        );

        var dashboard = new Dashboard(
            project.Id,
            "User2 dashboard"
        );

        await AddProjectAndDashboardAsync(
            project,
            dashboard
        );

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
}
