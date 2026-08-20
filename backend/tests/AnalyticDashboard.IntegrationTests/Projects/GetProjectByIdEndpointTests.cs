using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class GetProjectByIdEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public GetProjectByIdEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRequest(
        Guid projectId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}"
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

    private async Task AddProjectAsync(Project project)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);

        await dbContext.SaveChangesAsync(CancellationToken);
    }

    [Fact]
    public async Task GetProjectById_ShouldReturnOk_WhenProjectBelongsToAuthenticatedUser()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Get by id project"
        );

        await AddProjectAsync(project);

        using var request = CreateRequest(
            project.Id,
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
            .ReadFromJsonAsync<GetProjectByIdResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            project.Id,
            result.Id
        );

        Assert.Equal(
            project.Name,
            result.Name
        );

        Assert.NotEqual(
            default,
            result.CreatedAt
        );
    }

    [Fact]
    public async Task GetProjectById_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 project"
        );

        await AddProjectAsync(project);

        using var request = CreateRequest(
            project.Id,
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
    public async Task GetProjectById_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid()
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
    public async Task GetProjectById_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateRequest(
            Guid.NewGuid()
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
    public async Task GetProjectById_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateRequest(
            Guid.NewGuid(),
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
}
