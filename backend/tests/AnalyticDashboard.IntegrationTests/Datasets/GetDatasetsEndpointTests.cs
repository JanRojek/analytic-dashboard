using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Datasets;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
        Guid? userId = null,
        int? page = null,
        int? pageSize = null)
    {
        var queryParameters = new List<string>();

        if (page.HasValue)
        {
            queryParameters.Add($"page={page.Value}");
        }

        if (pageSize.HasValue)
        {
            queryParameters.Add($"pageSize={pageSize.Value}");
        }

        var url = $"/projects/{projectId}/datasets";

        if (queryParameters.Count > 0)
        {
            url += $"?{string.Join("&", queryParameters)}";
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            url
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

        await dbContext.SaveChangesAsync(CancellationToken);

        dbContext.Datasets.AddRange(datasets);

        await dbContext.SaveChangesAsync(CancellationToken);
    }

    private async Task<(Project Project, Dataset[] Datasets)> SeedDatasetsAsync(
        Guid userId,
        params string[] names)
    {
        var project = new Project(
            userId,
            "Pagination project"
        );

        var datasets = names
            .Select(name => new Dataset(project.Id, name))
            .ToArray();

        await AddDataAsync(
            [project],
            datasets
        );

        return (project, datasets);
    }

    private async Task SetCreatedAtUtcAsync(
        params (Dataset Dataset, DateTime CreatedAtUtc)[] values)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        foreach (var (dataset, createdAtUtc) in values)
        {
            var updatedCount = await dbContext.Datasets
                .Where(entity => entity.Id == dataset.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        entity => entity.CreatedAtUtc,
                        createdAtUtc
                    ),
                    CancellationToken
                );

            Assert.Equal(1, updatedCount);
        }
    }

    private async Task<GetDatasetsResponse> GetSuccessfulResponseAsync(
        Guid projectId,
        Guid userId,
        int? page = null,
        int? pageSize = null)
    {
        using var request = CreateGetRequest(
            projectId,
            userId,
            page,
            pageSize
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
            .ReadFromJsonAsync<GetDatasetsResponse>(
                CancellationToken
            );

        return Assert.IsType<GetDatasetsResponse>(result);
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

        var datasetA = new Dataset(
            requestedProject.Id,
            "Dataset A"
        );

        var datasetB = new Dataset(
            requestedProject.Id,
            "Dataset B"
        );

        var otherDataset = new Dataset(
            otherProject.Id,
            "Other dataset"
        );

        var foreignDataset = new Dataset(
            foreignProject.Id,
            "Foreign dataset"
        );

        await AddDataAsync(
            [requestedProject, otherProject, foreignProject],
            [datasetA, datasetB, otherDataset, foreignDataset]
        );

        var result = await GetSuccessfulResponseAsync(
            requestedProject.Id,
            userId
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(24, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalPages);

        Assert.Equivalent(
            expected: new[] { datasetA.Id, datasetB.Id },
            actual: result.Items.Select(item => item.Id),
            strict: true
        );

        var itemA = Assert.Single(
            result.Items,
            item => item.Id == datasetA.Id
        );

        Assert.Equal(datasetA.Name, itemA.Name);
        Assert.Null(itemA.CurrentVersion);

        Assert.InRange(
            itemA.CreatedAtUtc,
            datasetA.CreatedAtUtc.AddMilliseconds(-1),
            datasetA.CreatedAtUtc.AddMilliseconds(1)
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnEmptyPage_WhenProjectHasNoDatasets()
    {
        var userId = Guid.NewGuid();

        var (project, _) = await SeedDatasetsAsync(userId);

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId
        );

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(24, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreateGetRequest(
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
    public async Task GetDatasets_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "Foreign dataset project"
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

    [Fact]
    public async Task GetDatasets_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateGetRequest(
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
    public async Task GetDatasets_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateGetRequest(
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
    public async Task GetDatasets_ShouldReturnDatasetsOrderedByCreatedAtUtcDescending()
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Dataset A",
            "Dataset B",
            "Dataset C"
        );

        var baseDate = new DateTime(
            2026, 8, 20, 10, 0, 0, DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (datasets[0], baseDate),
            (datasets[1], baseDate.AddHours(2)),
            (datasets[2], baseDate.AddHours(1))
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId
        );

        Assert.Equal(
            new[]
            {
                datasets[1].Id,
                datasets[2].Id,
                datasets[0].Id
            },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnDatasetsOrderedById_WhenCreatedAtUtcIsEqual()
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Tie breaker A",
            "Tie breaker B"
        );

        var createdAtUtc = new DateTime(
            2026, 8, 20, 12, 0, 0, DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (datasets[0], createdAtUtc),
            (datasets[1], createdAtUtc)
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId
        );

        var expectedIds = datasets
            .Select(dataset => dataset.Id)
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(
            expectedIds,
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnRequestedPage()
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Pagination A",
            "Pagination B",
            "Pagination C",
            "Pagination D",
            "Pagination E"
        );

        var baseDate = new DateTime(
            2026, 8, 20, 10, 0, 0, DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (datasets[0], baseDate),
            (datasets[1], baseDate.AddHours(1)),
            (datasets[2], baseDate.AddHours(2)),
            (datasets[3], baseDate.AddHours(3)),
            (datasets[4], baseDate.AddHours(4))
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            page: 2,
            pageSize: 2
        );

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);

        Assert.Equal(
            new[]
            {
                datasets[2].Id,
                datasets[1].Id
            },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(999)]
    public async Task GetDatasets_ShouldReturnFirstPage_WhenPageIsOutOfRange(
        int page)
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Page A",
            "Page B",
            "Page C"
        );

        var baseDate = new DateTime(
            2026, 8, 21, 10, 0, 0, DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (datasets[0], baseDate),
            (datasets[1], baseDate.AddHours(1)),
            (datasets[2], baseDate.AddHours(2))
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            page: page,
            pageSize: 2
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);

        Assert.Equal(
            new[]
            {
                datasets[2].Id,
                datasets[1].Id
            },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldReturnAllDatasets_WhenPageSizeExceedsDatasetCount()
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Large page A",
            "Large page B",
            "Large page C"
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            pageSize: 50
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.TotalPages);

        Assert.Equivalent(
            expected: datasets.Select(dataset => dataset.Id),
            actual: result.Items.Select(item => item.Id),
            strict: true
        );
    }

    [Fact]
    public async Task GetDatasets_ShouldRespectDefaultPageSize()
    {
        var userId = Guid.NewGuid();

        var names = Enumerable.Range(1, 25)
            .Select(number => $"Dataset {number}")
            .ToArray();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            names
        );

        var firstPage = await GetSuccessfulResponseAsync(
            project.Id,
            userId
        );

        var secondPage = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            page: 2
        );

        Assert.Equal(24, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(24, firstPage.PageSize);
        Assert.Equal(25, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);

        Assert.Single(secondPage.Items);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(24, secondPage.PageSize);
        Assert.Equal(25, secondPage.TotalCount);
        Assert.Equal(2, secondPage.TotalPages);

        var returnedIds = firstPage.Items
            .Concat(secondPage.Items)
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(25, returnedIds.Distinct().Count());

        Assert.Equivalent(
            expected: datasets.Select(dataset => dataset.Id),
            actual: returnedIds,
            strict: true
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(97)]
    [InlineData(101)]
    [InlineData(200)]
    public async Task GetDatasets_ShouldNormalizePageSize_WhenPageSizeIsInvalid(
        int pageSize)
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Page size normalization"
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            pageSize: pageSize
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(24, result.PageSize);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);

        var item = Assert.Single(result.Items);

        Assert.Equal(datasets[0].Id, item.Id);
    }

    [Fact]
    public async Task GetDatasets_ShouldAcceptMaximumPageSize()
    {
        var userId = Guid.NewGuid();

        var (project, datasets) = await SeedDatasetsAsync(
            userId,
            "Maximum page size"
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            pageSize: 96
        );

        Assert.Equal(96, result.PageSize);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);

        var item = Assert.Single(result.Items);

        Assert.Equal(datasets[0].Id, item.Id);
    }
}
