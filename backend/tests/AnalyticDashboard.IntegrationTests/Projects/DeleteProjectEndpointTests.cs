using System.Net;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class DeleteProjectEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public DeleteProjectEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateDeleteRequest(
        Guid projectId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
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
    public async Task DeleteProject_ShouldReturnNoContent_WhenProjectBelongsToAuthenticatedUser()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Project to delete"
        );

        await AddProjectAsync(project);

        using var request = CreateDeleteRequest(
            project.Id,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Projects.AnyAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteProject_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 project"
        );

        await AddProjectAsync(project);

        using var request = CreateDeleteRequest(
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

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Projects.AnyAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.True(exists);
    }

    [Fact]
    public async Task DeleteProject_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreateDeleteRequest(
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
    public async Task DeleteProject_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateDeleteRequest(
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
    public async Task DeleteProject_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateDeleteRequest(
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

    [Fact]
    public async Task DeleteProject_ShouldReturnOneNoContentAndOneNotFound_WhenRequestsAreConcurrent()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Concurrent project"
        );

        await AddProjectAsync(project);

        using var firstRequest = CreateDeleteRequest(
            project.Id,
            userId
        );

        using var secondRequest = CreateDeleteRequest(
            project.Id,
            userId
        );

        var firstTask = _fixture.Client.SendAsync(
            firstRequest,
            CancellationToken
        );

        var secondTask = _fixture.Client.SendAsync(
            secondRequest,
            CancellationToken
        );

        var responses = await Task.WhenAll(
            firstTask,
            secondTask
        );

        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        var statusCodes = responses
            .Select(response => response.StatusCode)
            .ToArray();

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.NoContent)
        );

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.NotFound)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Projects.AnyAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.False(exists);
    }
}
