using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task DeleteProject_ShouldEnqueueCleanupForAllDatasets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var ownerId = Guid.NewGuid();

        var project = new Project(ownerId, "Project to delete");
        var otherProject = new Project(ownerId, "Unrelated project");

        var firstDataset = new Dataset(project.Id, "First dataset");
        var secondDataset = new Dataset(project.Id, "Second dataset");
        var unrelatedDataset = new Dataset(
            otherProject.Id,
            "Unrelated dataset"
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(
                project,
                otherProject,
                firstDataset,
                secondDataset,
                unrelatedDataset
            );

            await context.SaveChangesAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{project.Id}"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            ownerId.ToString()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            cancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope =
            _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var projectExists = await verificationContext.Projects
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == project.Id,
                cancellationToken
            );

        Assert.False(projectExists);

        var remainingDatasetIds = await verificationContext.Datasets
            .AsNoTracking()
            .Select(dataset => dataset.Id)
            .ToListAsync(cancellationToken);

        Assert.DoesNotContain(firstDataset.Id, remainingDatasetIds);
        Assert.DoesNotContain(secondDataset.Id, remainingDatasetIds);
        Assert.Contains(unrelatedDataset.Id, remainingDatasetIds);

        var cleanupDatasetIds = await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .Where(job =>
                job.DatasetId == firstDataset.Id
                || job.DatasetId == secondDataset.Id
                || job.DatasetId == unrelatedDataset.Id
            )
            .Select(job => job.DatasetId)
            .ToListAsync(cancellationToken);

        Assert.Equal(2, cleanupDatasetIds.Count);
        Assert.Contains(firstDataset.Id, cleanupDatasetIds);
        Assert.Contains(secondDataset.Id, cleanupDatasetIds);
        Assert.DoesNotContain(unrelatedDataset.Id, cleanupDatasetIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteProject_ShouldReturnConflict_WhenDatasetImportIsActive(bool running)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Project with active import");
        var dataset = new Dataset(project.Id, "Importing dataset");

        var version = new DatasetVersion(
            dataset.Id,
            1,
            "source.csv"
        );

        var importJob = new ImportJob(
            version.Id,
            "source.csv",
            $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
        );

        if (running)
        {
            importJob.MarkRunning();
        }

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(project, dataset, version, importJob);

            await context.SaveChangesAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{project.Id}"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            ownerId.ToString()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            cancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken
        );

        Assert.Equal(
            "project_import_in_progress",
            problem.GetProperty("code").GetString()
        );

        await using var verificationScope =
            _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.True(await verificationContext.Projects
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == project.Id,
                cancellationToken
            ));

        Assert.True(await verificationContext.Datasets
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == dataset.Id,
                cancellationToken
            ));

        var savedJob = await verificationContext.ImportJobs
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == importJob.Id,
                cancellationToken
            );

        Assert.Equal(importJob.Status, savedJob.Status);

        Assert.False(await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.DatasetId == dataset.Id,
                cancellationToken
            ));
    }
}
