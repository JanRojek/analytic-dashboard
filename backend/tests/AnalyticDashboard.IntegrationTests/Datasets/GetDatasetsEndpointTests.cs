using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class GetDatasetsEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetDatasetsEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateGetRequest(
        Guid projectId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}/datasets"
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

    private async Task AddDataAsync(
        IReadOnlyCollection<Project> projects,
        IReadOnlyCollection<Dataset> datasets)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.AddRange(projects);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        dbContext.Datasets.AddRange(datasets);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnOnlyDatasetsFromRequestedOwnedProject()
    {
        var userId = Guid.NewGuid();

        var requestedProject = new Project(
            userId,
            "Requested project"
        );

        var otherProject = new Project(
            userId,
            "Other project"
        );

        var foreignProject = new Project(
            Guid.NewGuid(),
            "Foreign project"
        );

        var datasetA = CreateDataset(
            requestedProject.Id,
            "Dataset A"
        );

        var datasetB = CreateDataset(
            requestedProject.Id,
            "Dataset B"
        );

        var otherDataset = CreateDataset(
            otherProject.Id,
            "Other dataset"
        );

        var foreignDataset = CreateDataset(
            foreignProject.Id,
            "Foreign dataset"
        );

        await AddDataAsync(
            [
                requestedProject,
                otherProject,
                foreignProject
            ],
            [
                datasetA,
                datasetB,
                otherDataset,
                foreignDataset
            ]
        );

        using var request = CreateGetRequest(
            requestedProject.Id,
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
            .ReadFromJsonAsync<IReadOnlyList<DatasetResponse>>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equivalent(
            expected: new[]
            {
                datasetA.Id,
                datasetB.Id
            },
            actual: result.Select(dataset => dataset.Id),
            strict: true
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 dataset project"
        );

        await AddDataAsync(
            [project],
            []
        );

        using var request = CreateGetRequest(
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
