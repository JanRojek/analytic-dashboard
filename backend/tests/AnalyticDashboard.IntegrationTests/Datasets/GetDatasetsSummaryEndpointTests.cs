using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class GetDatasetsSummaryEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetDatasetsSummaryEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRequest(
        Guid projectId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/projects/{projectId}/datasets/summary"
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

    private async Task<GetDatasetsSummaryResponse> GetSummaryAsync(
        Guid projectId,
        Guid userId)
    {
        using var request = CreateRequest(projectId, userId);
        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content
            .ReadFromJsonAsync<GetDatasetsSummaryResponse>(
                CancellationToken
            );

        return Assert.IsType<GetDatasetsSummaryResponse>(summary);
    }

    private async Task SeedAsync(
        IReadOnlyCollection<Project> projects,
        IReadOnlyCollection<Dataset> datasets)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.AddRange(projects);
        await dbContext.SaveChangesAsync(CancellationToken);

        dbContext.Datasets.AddRange(datasets);
        await dbContext.SaveChangesAsync(CancellationToken);
    }

    private async Task SetCurrentVersionsAsync(
        IReadOnlyCollection<DatasetVersion> versions,
        params (Dataset Dataset, DatasetVersion Current)[] assignments)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.DatasetVersions.AddRange(versions);
        await dbContext.SaveChangesAsync(CancellationToken);

        foreach (var (dataset, current) in assignments)
        {
            var updatedCount = await dbContext.Datasets
                .Where(item => item.Id == dataset.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.CurrentVersionId,
                        current.Id
                    ),
                    CancellationToken
                );

            Assert.Equal(1, updatedCount);
        }
    }

    [Fact]
    public async Task Summary_ShouldReturnZeros_WhenProjectIsEmpty()
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Empty project");

        await SeedAsync([project], []);

        var summary = await GetSummaryAsync(project.Id, userId);

        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.ReadyCount);
        Assert.Equal(0L, summary.TotalRows);
    }

    [Fact]
    public async Task Summary_ShouldCountOnlyReadyCurrentVersions()
    {
        var userId = Guid.NewGuid();

        var project = new Project(userId, "Summary project");
        var otherProject = new Project(userId, "Other project");
        var foreignProject = new Project(Guid.NewGuid(), "Foreign project");

        var readyA = new Dataset(project.Id, "Ready A");
        var readyB = new Dataset(project.Id, "Ready B");
        var pending = new Dataset(project.Id, "Pending");
        var failed = new Dataset(project.Id, "Failed");
        var importingNewVersion = new Dataset(project.Id, "New version");
        var withoutVersion = new Dataset(project.Id, "No version");

        var otherDataset = new Dataset(otherProject.Id, "Other");
        var foreignDataset = new Dataset(foreignProject.Id, "Foreign");

        await SeedAsync(
            [project, otherProject, foreignProject],
            [
                readyA,
                readyB,
                pending,
                failed,
                importingNewVersion,
                withoutVersion,
                otherDataset,
                foreignDataset
            ]
        );

        var versionA = new DatasetVersion(readyA.Id, 1, "a.csv");
        versionA.MarkReady("tests/a.parquet", 120, 4);

        var versionB = new DatasetVersion(readyB.Id, 1, "b.csv");
        versionB.MarkReady("tests/b.parquet", 80, 3);

        var pendingVersion = new DatasetVersion(
            pending.Id, 1, "pending.csv"
        );

        var failedVersion = new DatasetVersion(
            failed.Id, 1, "failed.csv"
        );
        failedVersion.MarkFailed();

        var oldReadyVersion = new DatasetVersion(
            importingNewVersion.Id, 1, "old.csv"
        );
        oldReadyVersion.MarkReady("tests/old.parquet", 900, 2);

        var newPendingVersion = new DatasetVersion(
            importingNewVersion.Id, 2, "new.csv"
        );

        var otherVersion = new DatasetVersion(
            otherDataset.Id, 1, "other.csv"
        );
        otherVersion.MarkReady("tests/other.parquet", 500, 2);

        var foreignVersion = new DatasetVersion(
            foreignDataset.Id, 1, "foreign.csv"
        );
        foreignVersion.MarkReady("tests/foreign.parquet", 1000, 2);

        await SetCurrentVersionsAsync(
            [
                versionA,
                versionB,
                pendingVersion,
                failedVersion,
                oldReadyVersion,
                newPendingVersion,
                otherVersion,
                foreignVersion
            ],
            (readyA, versionA),
            (readyB, versionB),
            (pending, pendingVersion),
            (failed, failedVersion),
            (importingNewVersion, newPendingVersion),
            (otherDataset, otherVersion),
            (foreignDataset, foreignVersion)
        );

        var summary = await GetSummaryAsync(project.Id, userId);

        Assert.Equal(6, summary.TotalCount);
        Assert.Equal(2, summary.ReadyCount);
        Assert.Equal(200L, summary.TotalRows);
    }

    [Fact]
    public async Task Summary_ShouldUseLongForTotalRows()
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Large datasets");

        var first = new Dataset(project.Id, "First");
        var second = new Dataset(project.Id, "Second");

        await SeedAsync([project], [first, second]);

        var firstVersion = new DatasetVersion(first.Id, 1, "first.csv");
        firstVersion.MarkReady("tests/first.parquet", 1_600_000_000, 2);

        var secondVersion = new DatasetVersion(second.Id, 1, "second.csv");
        secondVersion.MarkReady("tests/second.parquet", 1_100_000_000, 2);

        await SetCurrentVersionsAsync(
            [firstVersion, secondVersion],
            (first, firstVersion),
            (second, secondVersion)
        );

        var summary = await GetSummaryAsync(project.Id, userId);

        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(2, summary.ReadyCount);
        Assert.Equal(2_700_000_000L, summary.TotalRows);
    }

    [Fact]
    public async Task Summary_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Summary_ShouldReturnNotFound_WhenProjectIsNotOwned()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "Foreign project"
        );

        await SeedAsync([project], []);

        using var request = CreateRequest(
            project.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Summary_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        using var request = CreateRequest(Guid.NewGuid());

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
