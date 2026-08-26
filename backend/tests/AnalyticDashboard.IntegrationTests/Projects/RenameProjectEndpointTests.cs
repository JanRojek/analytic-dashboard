using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class RenameProjectEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public static TheoryData<string, string> InvalidNames =>
        new()
        {
            {
                "   ",
                "Project name cannot be empty."
            },
            {
                new string(
                    'a',
                    Project.MaxNameLength + 1
                ),
                $"Project name cannot be longer than {Project.MaxNameLength} characters."
            }
        };

    public RenameProjectEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePatchRequest(
        Guid projectId,
        string name,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/projects/{projectId}"
        )
        {
            Content = JsonContent.Create(
                new RenameProjectRequest(name)
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

    private async Task AddProjectsAsync(params Project[] projects)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.AddRange(projects);

        await dbContext.SaveChangesAsync(CancellationToken);
    }

    [Fact]
    public async Task RenameProject_ShouldReturnOk_WhenProjectBelongsToAuthenticatedUser()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Project to rename"
        );

        await AddProjectsAsync(project);

        using var request = CreatePatchRequest(
            project.Id,
            "   Renamed project   ",
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
            .ReadFromJsonAsync<RenameProjectResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            project.Id,
            result.Id
        );

        Assert.Equal(
            "Renamed project",
            result.Name
        );

        Assert.Equal(
            project.CreatedAtUtc,
            result.CreatedAtUtc,
            TimeSpan.FromMilliseconds(1)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == result.Id,
            CancellationToken
        );

        Assert.Equal(
            "Renamed project",
            projectInDb.Name
        );
    }

    [Fact]
    public async Task RenameProject_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 project"
        );

        await AddProjectsAsync(project);

        using var request = CreatePatchRequest(
            project.Id,
            "User2 renamed project",
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

        var projectInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.Equal(
            "User2 project",
            projectInDb.Name
        );
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task RenameProject_ShouldReturnBadRequest_WhenNameIsInvalid(
        string name,
        string expectedError)
    {
        var userId = Guid.NewGuid();

        const string originalName = "My project";

        var project = new Project(
            userId,
            originalName
        );

        await AddProjectsAsync(project);

        using var request = CreatePatchRequest(
            project.Id,
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
            expectedError,
            errors[0]
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.Equal(
            originalName,
            projectInDb.Name
        );
    }

    [Fact]
    public async Task RenameProject_ShouldReturnConflict_WhenNameAlreadyExistsIgnoringCase()
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

        await AddProjectsAsync(
            projectA,
            projectB
        );

        using var request = CreatePatchRequest(
            projectB.Id,
            "project a",
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
            "Project 'project a' already exists.",
            problem.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectAInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == projectA.Id,
            CancellationToken
        );

        var projectBInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == projectB.Id,
            CancellationToken
        );

        Assert.Equal(
            "Project A",
            projectAInDb.Name
        );

        Assert.Equal(
            "Project B",
            projectBInDb.Name
        );
    }

    [Fact]
    public async Task RenameProject_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreatePatchRequest(
            Guid.NewGuid(),
            "My project",
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
    public async Task RenameProject_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreatePatchRequest(
            Guid.NewGuid(),
            "My project"
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
    public async Task RenameProject_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreatePatchRequest(
            Guid.NewGuid(),
            "My project",
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
    public async Task RenameProject_ShouldHandleConcurrentRenameAndDelete()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Concurrent project"
        );

        await AddProjectsAsync(project);

        using var deleteRequest = CreateDeleteRequest(
            project.Id,
            userId
        );

        using var patchRequest = CreatePatchRequest(
            project.Id,
            "New name",
            userId
        );

        var deleteTask = _fixture.Client.SendAsync(
            deleteRequest,
            CancellationToken
        );

        var patchTask = _fixture.Client.SendAsync(
            patchRequest,
            CancellationToken
        );

        var responses = await Task.WhenAll(
            deleteTask,
            patchTask
        );

        var deleteStatus = responses[0].StatusCode;
        var patchStatus = responses[1].StatusCode;

        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteStatus
        );

        Assert.Contains(
            patchStatus,
            new[]
            {
                HttpStatusCode.OK,
                HttpStatusCode.NotFound
            }
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
    public async Task RenameProject_ShouldReturnOneOkAndOneConflict_WhenRequestsAreConcurrent()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Concurrent rename A"
        );

        var projectB = new Project(
            userId,
            "Concurrent rename B"
        );

        await AddProjectsAsync(
            projectA,
            projectB
        );

        using var patchARequest = CreatePatchRequest(
            projectA.Id,
            "Concurrent shared name",
            userId
        );

        using var patchBRequest = CreatePatchRequest(
            projectB.Id,
            "Concurrent shared name",
            userId
        );

        var patchATask = _fixture.Client.SendAsync(
            patchARequest,
            CancellationToken
        );

        var patchBTask = _fixture.Client.SendAsync(
            patchBRequest,
            CancellationToken
        );

        var responses = await Task.WhenAll(
            patchATask,
            patchBTask
        );

        using var patchAResponse = responses[0];

        var statusCodes = responses
            .Select(response => response.StatusCode)
            .ToArray();

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.OK)
        );

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.Conflict)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectAInDb = await dbContext.Projects.SingleAsync(
            project => project.Id == projectA.Id,
            CancellationToken
        );

        var projectBInDb = await dbContext.Projects.SingleAsync(
            project => project.Id == projectB.Id,
            CancellationToken
        );

        if (patchAResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(
                "Concurrent shared name",
                projectAInDb.Name
            );

            Assert.Equal(
                "Concurrent rename B",
                projectBInDb.Name
            );
        }
        else
        {
            Assert.Equal(
                "Concurrent rename A",
                projectAInDb.Name
            );

            Assert.Equal(
                "Concurrent shared name",
                projectBInDb.Name
            );
        }
    }

    [Fact]
    public async Task RenameProject_ShouldReturnOk_WhenNameIsUsedByDifferentOwner()
    {
        var projectA = new Project(
            TestAuthHandler.User1Id,
            "Cross owner rename source"
        );

        var projectB = new Project(
            TestAuthHandler.User2Id,
            "Cross owner shared name"
        );

        await AddProjectsAsync(
            projectA,
            projectB
        );

        using var request = CreatePatchRequest(
            projectA.Id,
            "Cross owner shared name",
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

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectAInDb = await dbContext.Projects.SingleAsync(
            project => project.Id == projectA.Id,
            CancellationToken
        );

        var projectBInDb = await dbContext.Projects.SingleAsync(
            project => project.Id == projectB.Id,
            CancellationToken
        );

        Assert.Equal(
            "Cross owner shared name",
            projectAInDb.Name
        );

        Assert.Equal(
            "Cross owner shared name",
            projectBInDb.Name
        );
    }

    [Fact]
    public async Task RenameProject_ShouldReturnOkForBoth_WhenSameProjectIsRenamedConcurrently()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Concurrent same project"
        );

        await AddProjectsAsync(project);

        using var firstRequest = CreatePatchRequest(
            project.Id,
            "Concurrent rename first",
            userId
        );

        using var secondRequest = CreatePatchRequest(
            project.Id,
            "Concurrent rename second",
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

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode
        );

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.Contains(
            projectInDb.Name,
            new[]
            {
                "Concurrent rename first",
                "Concurrent rename second"
            }
        );
    }

    [Fact]
    public async Task RenameProject_ShouldReturnBadRequest_WhenNameIsNull()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Original name"
        );

        await AddProjectsAsync(project);

        using var request = CreatePatchRequest(
            project.Id,
            null!,
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

        var projectInDb = await dbContext.Projects.SingleAsync(
            entity => entity.Id == project.Id,
            CancellationToken
        );

        Assert.Equal(
            "Original name",
            projectInDb.Name
        );
    }
}
