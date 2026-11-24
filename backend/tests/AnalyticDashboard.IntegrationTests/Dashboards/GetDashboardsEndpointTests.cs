using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Dashboards;

public sealed class GetDashboardsEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public GetDashboardsEndpointTests(ApiFixture fixture)
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

        var url = $"/projects/{projectId}/dashboards";

        if (queryParameters.Count > 0)
        {
            url += $"?{string.Join("&", queryParameters)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);

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
        IReadOnlyCollection<Dashboard> dashboards)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.AddRange(projects);
        await dbContext.SaveChangesAsync(CancellationToken);

        dbContext.Dashboards.AddRange(dashboards);
        await dbContext.SaveChangesAsync(CancellationToken);
    }

    private async Task<(Project Project, Dashboard[] Dashboards)> SeedDashboardsAsync(
        Guid userId,
        params string[] names)
    {
        var project = new Project(userId, "Pagination project");

        var dashboards = names
            .Select(name => new Dashboard(project.Id, name))
            .ToArray();

        await AddDataAsync([project], dashboards);

        return (project, dashboards);
    }

    private async Task SetCreatedAtUtcAsync(
        params (Dashboard Dashboard, DateTime CreatedAtUtc)[] values)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        foreach (var (dashboard, createdAtUtc) in values)
        {
            var updatedCount = await dbContext.Dashboards
                .Where(entity => entity.Id == dashboard.Id)
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

    private async Task<GetDashboardsResponse> GetSuccessfulResponseAsync(
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<GetDashboardsResponse>(CancellationToken);

        return Assert.IsType<GetDashboardsResponse>(result);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnOnlyDashboardsFromRequestedOwnedProject()
    {
        var userId = Guid.NewGuid();
        var requestedProject = new Project(userId, "Requested project");
        var otherProject = new Project(userId, "Other project");
        var foreignProject = new Project(Guid.NewGuid(), "Foreign project");

        var dashboardA = new Dashboard(requestedProject.Id, "Dashboard A");
        var dashboardB = new Dashboard(requestedProject.Id, "Dashboard B");
        var otherDashboard = new Dashboard(otherProject.Id, "Other dashboard");
        var foreignDashboard = new Dashboard(foreignProject.Id, "Foreign dashboard");

        await AddDataAsync(
            [requestedProject, otherProject, foreignProject],
            [dashboardA, dashboardB, otherDashboard, foreignDashboard]
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
            expected: new[] { dashboardA.Id, dashboardB.Id },
            actual: result.Items.Select(item => item.Id),
            strict: true
        );

        var itemA = Assert.Single(
            result.Items,
            item => item.Id == dashboardA.Id
        );

        Assert.Equal(dashboardA.ProjectId, itemA.ProjectId);
        Assert.Equal(dashboardA.Name, itemA.Name);
        Assert.InRange(
            itemA.CreatedAtUtc,
            dashboardA.CreatedAtUtc.AddMilliseconds(-1),
            dashboardA.CreatedAtUtc.AddMilliseconds(1)
        );
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnEmptyPage_WhenProjectHasNoDashboards()
    {
        var userId = Guid.NewGuid();
        var (project, _) = await SeedDashboardsAsync(userId);

        var result = await GetSuccessfulResponseAsync(project.Id, userId);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(24, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnNotFound_WhenProjectDoesNotExist()
    {
        using var request = CreateGetRequest(Guid.NewGuid(), Guid.NewGuid());
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnNotFound_WhenProjectBelongsToDifferentUser()
    {
        var project = new Project(TestAuthHandler.User2Id, "Foreign dashboard project");
        await AddDataAsync([project], []);

        using var request = CreateGetRequest(
            project.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateGetRequest(Guid.NewGuid());
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateGetRequest(Guid.NewGuid(), Guid.Empty);
        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnDashboardsOrderedByCreatedAtUtcDescending()
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
            userId,
            "Dashboard A",
            "Dashboard B",
            "Dashboard C"
        );

        var baseDate = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

        await SetCreatedAtUtcAsync(
            (dashboards[0], baseDate),
            (dashboards[1], baseDate.AddHours(2)),
            (dashboards[2], baseDate.AddHours(1))
        );

        var result = await GetSuccessfulResponseAsync(project.Id, userId);

        Assert.Equal(
            new[] { dashboards[1].Id, dashboards[2].Id, dashboards[0].Id },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnDashboardsOrderedById_WhenCreatedAtUtcIsEqual()
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
            userId,
            "Tie breaker A",
            "Tie breaker B"
        );

        var createdAtUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        await SetCreatedAtUtcAsync(
            (dashboards[0], createdAtUtc),
            (dashboards[1], createdAtUtc)
        );

        var result = await GetSuccessfulResponseAsync(project.Id, userId);

        Assert.Equal(
            dashboards.Select(dashboard => dashboard.Id).OrderBy(id => id).ToArray(),
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnRequestedPage()
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
            userId,
            "Pagination A",
            "Pagination B",
            "Pagination C",
            "Pagination D",
            "Pagination E"
        );

        var baseDate = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

        await SetCreatedAtUtcAsync(
            (dashboards[0], baseDate),
            (dashboards[1], baseDate.AddHours(1)),
            (dashboards[2], baseDate.AddHours(2)),
            (dashboards[3], baseDate.AddHours(3)),
            (dashboards[4], baseDate.AddHours(4))
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
            new[] { dashboards[2].Id, dashboards[1].Id },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(999)]
    public async Task GetDashboards_ShouldReturnFirstPage_WhenPageIsOutOfRange(int page)
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
            userId,
            "Page A",
            "Page B",
            "Page C"
        );

        var baseDate = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);

        await SetCreatedAtUtcAsync(
            (dashboards[0], baseDate),
            (dashboards[1], baseDate.AddHours(1)),
            (dashboards[2], baseDate.AddHours(2))
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
            new[] { dashboards[2].Id, dashboards[1].Id },
            result.Items.Select(item => item.Id).ToArray()
        );
    }

    [Fact]
    public async Task GetDashboards_ShouldReturnAllDashboards_WhenPageSizeExceedsDashboardCount()
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
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
            expected: dashboards.Select(dashboard => dashboard.Id),
            actual: result.Items.Select(item => item.Id),
            strict: true
        );
    }

    [Fact]
    public async Task GetDashboards_ShouldRespectDefaultPageSize()
    {
        var userId = Guid.NewGuid();
        var names = Enumerable.Range(1, 25)
            .Select(number => $"Dashboard {number}")
            .ToArray();

        var (project, dashboards) = await SeedDashboardsAsync(userId, names);

        var firstPage = await GetSuccessfulResponseAsync(project.Id, userId);
        var secondPage = await GetSuccessfulResponseAsync(project.Id, userId, page: 2);

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
            expected: dashboards.Select(dashboard => dashboard.Id),
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
    public async Task GetDashboards_ShouldNormalizePageSize_WhenPageSizeIsInvalid(
        int pageSize)
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
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
        Assert.Equal(dashboards[0].Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetDashboards_ShouldAcceptMaximumPageSize()
    {
        var userId = Guid.NewGuid();
        var (project, dashboards) = await SeedDashboardsAsync(
            userId,
            "Maximum page size"
        );

        var result = await GetSuccessfulResponseAsync(
            project.Id,
            userId,
            pageSize: 96
        );

        Assert.Equal(1, result.Page);
        Assert.Equal(96, result.PageSize);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(dashboards[0].Id, Assert.Single(result.Items).Id);
    }
}
