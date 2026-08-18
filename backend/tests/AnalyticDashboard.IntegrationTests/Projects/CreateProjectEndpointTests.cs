using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Application.Projects.CreateProject;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class CreateProjectEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateProjectEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRequest(
        string name,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/projects"
        )
        {
            Content = JsonContent.Create(
                new CreateProjectRequest(name)
            )
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

    [Fact]
    public async Task CreateProject_ShouldReturnCreated()
    {
        using var request = CreateRequest(
            "   Happy path project   ",
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<CreateProjectResult.Success>(
                TestContext.Current.CancellationToken
            );

        Assert.NotNull(result);

        Assert.NotEqual(
            Guid.Empty,
            result.Id
        );

        Assert.Equal(
            "Happy path project",
            result.Name
        );

        Assert.NotEqual(
            default,
            result.CreatedAt
        );

        Assert.Equal(
            $"/projects/{result.Id}",
            response.Headers.Location?.ToString()
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var project = await dbContext.Projects.SingleAsync(
            project => project.Id == result.Id,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            TestAuthHandler.User1Id,
            project.OwnerId
        );

        Assert.Equal(
            "Happy path project",
            project.Name
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnBadRequest_WhenNameIsWhitespace()
    {
        using var request = CreateRequest(
            "   ",
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnBadRequest_WhenNameIsTooLong()
    {
        var name = new string(
            'a',
            Project.MaxNameLength + 1
        );

        using var request = CreateRequest(
            name,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnConflict_WhenNameAlreadyExistsIgnoringCase()
    {
        using var firstRequest = CreateRequest(
            "   Case insensitive project   ",
            TestAuthHandler.User1Id
        );

        using var firstResponse = await _fixture.Client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode
        );

        using var secondRequest = CreateRequest(
            "case insensitive project",
            TestAuthHandler.User1Id
        );

        using var secondResponse = await _fixture.Client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateRequest(
            "Unauthorized project"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnCreated_ForSameNameWhenOwnersAreDifferent()
    {
        using var firstRequest = CreateRequest(
            "Shared project",
            TestAuthHandler.User1Id
        );

        using var firstResponse = await _fixture.Client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode
        );

        using var secondRequest = CreateRequest(
            "Shared project",
            TestAuthHandler.User2Id
        );

        using var secondResponse = await _fixture.Client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            secondResponse.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnOneCreatedAndOneConflict_WhenRequestsAreConcurrent()
    {
        using var firstRequest = CreateRequest(
            "Concurrent project",
            TestAuthHandler.User1Id
        );

        using var secondRequest = CreateRequest(
            "Concurrent project",
            TestAuthHandler.User1Id
        );

        var firstTask = _fixture.Client.SendAsync(
            firstRequest,
            TestContext.Current.CancellationToken
        );

        var secondTask = _fixture.Client.SendAsync(
            secondRequest,
            TestContext.Current.CancellationToken
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
            statusCodes.Count(code => code == HttpStatusCode.Created)
        );

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.Conflict)
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateRequest(
            "Empty owner project",
            Guid.Empty
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }
}
