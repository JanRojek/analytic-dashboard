using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class GetProjectsEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public GetProjectsEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRequest(
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/projects"
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

    private async Task AddProjectsAsync(params Project[] projects)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.AddRange(projects);

        await dbContext.SaveChangesAsync(CancellationToken);
    }

    [Fact]
    public async Task GetProjects_ShouldReturnOnlyProjectsOwnedByAuthenticatedUser()
    {
        var projectA = new Project(
            TestAuthHandler.User1Id,
            "Project A"
        );

        var projectB = new Project(
            TestAuthHandler.User1Id,
            "Project B"
        );

        var projectC = new Project(
            TestAuthHandler.User2Id,
            "Project C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        using var request = CreateRequest(
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equivalent(
            expected: new[] { projectA.Id, projectB.Id },
            actual: result.Items.Select(x => x.Id),
            strict: true
        );

        var projectAItem = Assert.Single(
            result.Items,
            item => item.Id == projectA.Id
        );

        Assert.Equal(
            projectA.Name,
            projectAItem.Name
        );

        Assert.InRange(
            projectAItem.CreatedAt,
            projectA.CreatedAt.AddMilliseconds(-1),
            projectA.CreatedAt.AddMilliseconds(1)
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnEmptyList_WhenUserHasNoProjects()
    {
        using var request = CreateRequest(
            Guid.NewGuid()
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetProjects_ShouldReturnProjectsOrderedByCreatedAtDescending()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Project A"
        );

        var projectB = new Project(
            userId,
            "Project B"
        );

        var projectC = new Project(
            userId,
            "Project C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        using var request = CreateRequest(
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        var expectedIds = result.Items
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToArray();

        var actualIds = result.Items
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(
            expectedIds,
            actualIds
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateRequest();

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateRequest(
            Guid.Empty
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetProjects_ShouldOrderById_WhenCreatedAtIsEqual()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Tie breaker A"
        );

        var projectB = new Project(
            userId,
            "Tie breaker B"
        );

        await AddProjectsAsync(
            projectA,
            projectB
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var createdAt = new DateTime(
                2026,
                8,
                20,
                12,
                0,
                0,
                DateTimeKind.Utc
            );

            await dbContext.Projects
                .Where(project =>
                    project.Id == projectA.Id
                    || project.Id == projectB.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.CreatedAt,
                        createdAt
                    ),
                    CancellationToken
                );
        }

        using var request = CreateRequest(
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        var expectedIds = new[]
            {
                projectA.Id,
                projectB.Id
            }
            .OrderBy(id => id)
            .ToArray();

        var actualIds = result.Items
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(
            expectedIds,
            actualIds
        );
    }
}
