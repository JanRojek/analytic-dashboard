using System.Net;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class DeleteDatasetEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DeleteDatasetEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateDeleteRequest(
        Guid projectId,
        Guid datasetId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{projectId}/datasets/{datasetId}"
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

    private async Task AddDatasetAsync(
        Project project,
        Dataset dataset)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        dbContext.Datasets.Add(dataset);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task DeleteDataset_ShouldReturnNoContent_WhenDatasetBelongsToAuthenticatedUser()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Delete dataset project"
        );

        var dataset = CreateDataset(
            project.Id,
            "Dataset to delete"
        );

        await AddDatasetAsync(
            project,
            dataset
        );

        using var request = CreateDeleteRequest(
            project.Id,
            dataset.Id,
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

        var exists = await dbContext.Datasets.AnyAsync(
            entity => entity.Id == dataset.Id,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteDataset_ShouldReturnNotFound_WhenDatasetBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 delete project"
        );

        var dataset = CreateDataset(
            project.Id,
            "Private dataset"
        );

        await AddDatasetAsync(
            project,
            dataset
        );

        using var request = CreateDeleteRequest(
            project.Id,
            dataset.Id,
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

        var exists = await dbContext.Datasets.AnyAsync(
            entity => entity.Id == dataset.Id,
            CancellationToken
        );

        Assert.True(exists);
    }

    private static Dataset CreateDataset(
        Guid projectId,
        string name)
    {
        return new Dataset(
            projectId,
            name
        );
    }
}
