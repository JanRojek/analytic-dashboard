using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class CreateProjectEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public CreateProjectEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePostRequest(
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

    private async Task AddProjectAsync(Project project)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);

        await dbContext.SaveChangesAsync(CancellationToken);
    }

    [Fact]
    public async Task CreateProject_ShouldReturnCreated_WhenRequestIsValid()
    {
        var userId = Guid.NewGuid();

        using var request = CreatePostRequest(
            "   Happy path project   ",
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<CreateProjectResponse>(
                CancellationToken
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

        Assert.Equal(
            $"/projects/{result.Id}",
            response.Headers.Location?.ToString()
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var project = await dbContext.Projects.SingleAsync(
            project => project.Id == result.Id,
            CancellationToken
        );

        Assert.Equal(
            userId,
            project.OwnerId
        );

        Assert.Equal(
            "Happy path project",
            project.Name
        );

        Assert.Equal(
            project.CreatedAtUtc,
            result.CreatedAtUtc,
            TimeSpan.FromMilliseconds(1)
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnBadRequest_WhenNameIsWhitespace()
    {
        var userId = Guid.NewGuid();

        using var request = CreatePostRequest(
            "   ",
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(problem);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problem.Status
        );

        Assert.True(
            problem.Errors.TryGetValue(
                "Name",
                out var errors
            )
        );

        Assert.Single(errors);

        Assert.Equal(
            "Project name cannot be empty.",
            errors[0]
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Projects.AnyAsync(
            project => project.OwnerId == userId,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task CreateProject_ShouldReturnBadRequest_WhenNameIsTooLong()
    {
        var userId = Guid.NewGuid();

        var name = new string(
            'a',
            Project.MaxNameLength + 1
        );

        using var request = CreatePostRequest(
            name,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(problem);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problem.Status
        );

        Assert.True(
            problem.Errors.TryGetValue(
                "Name",
                out var errors
            )
        );

        Assert.Single(errors);

        Assert.Equal(
            $"Project name cannot be longer than {Project.MaxNameLength} characters.",
            errors[0]
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Projects.AnyAsync(
            project => project.OwnerId == userId,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task CreateProject_ShouldReturnConflict_WhenNameAlreadyExistsIgnoringCase()
    {
        var userId = Guid.NewGuid();

        var existingProject = new Project(
            userId,
            "Case insensitive project"
        );

        await AddProjectAsync(existingProject);

        using var request = CreatePostRequest(
            "case insensitive project",
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode
        );

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );

        var problem = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(problem);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            problem.Status
        );

        Assert.Equal(
            "Project name already exists.",
            problem.Title
        );

        Assert.Equal(
            "Project 'case insensitive project' already exists.",
            problem.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectInDb = await dbContext.Projects.SingleAsync(
            project => project.OwnerId == userId,
            CancellationToken
        );

        Assert.Equal(
            existingProject.Id,
            projectInDb.Id
        );

        Assert.Equal(
            "Case insensitive project",
            projectInDb.Name
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreatePostRequest(
            "Unauthorized project"
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
    public async Task CreateProject_ShouldReturnCreated_WhenSameNameIsUsedByDifferentOwners()
    {
        using var firstRequest = CreatePostRequest(
            "Shared project",
            TestAuthHandler.User1Id
        );

        using var firstResponse = await _fixture.Client.SendAsync(
            firstRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode
        );

        using var secondRequest = CreatePostRequest(
            "Shared project",
            TestAuthHandler.User2Id
        );

        using var secondResponse = await _fixture.Client.SendAsync(
            secondRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            secondResponse.StatusCode
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnOneCreatedAndOneConflict_WhenRequestsAreConcurrent()
    {
        var userId = Guid.NewGuid();

        using var firstRequest = CreatePostRequest(
            "Concurrent project",
            userId
        );

        using var secondRequest = CreatePostRequest(
            "Concurrent project",
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

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectsCount = await dbContext.Projects.CountAsync(
            project => project.OwnerId == userId
                       && project.Name == "Concurrent project",
            CancellationToken
        );

        Assert.Equal(
            1,
            projectsCount
        );
    }

    [Fact]
    public async Task CreateProject_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreatePostRequest(
            "Empty owner project",
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
    public async Task CreateProject_ShouldReturnUnauthorized_WhenUserIdIsMalformed()
    {
        using var request = CreatePostRequest(
            "Malformed user project"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            "not-a-guid"
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
