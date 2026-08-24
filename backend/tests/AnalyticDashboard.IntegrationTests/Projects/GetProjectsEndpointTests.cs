using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;
using AnalyticDashboard.Application.Projects.GetProjects;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class GetProjectsEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public GetProjectsEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateGetRequest(
        Guid? userId = null,
        int? page = null,
        int? pageSize = null)
    {
        var queryParameters = new List<string>();

        if (page.HasValue)
        {
            queryParameters.Add(
                $"page={page.Value}"
            );
        }

        if (pageSize.HasValue)
        {
            queryParameters.Add(
                $"pageSize={pageSize.Value}"
            );
        }

        var url = "/projects";

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

    private async Task SetCreatedAtUtcAsync(
        params (Project Project, DateTime CreatedAtUtc)[] values)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        foreach (var (project, createdAtUtc) in values)
        {
            var updatedCount = await dbContext.Projects
                .Where(entity => entity.Id == project.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        entity => entity.CreatedAtUtc,
                        createdAtUtc
                    ),
                    CancellationToken
                );

            Assert.Equal(
                1,
                updatedCount
            );
        }
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
    public async Task GetProjects_ShouldReturnOnlyProjectsOwnedByAuthenticatedUser()
    {
        var projectA = new Project(
            TestAuthHandler.User1Id,
            "Project A"
        );

        var projectB = new Project(
            TestAuthHandler.User1Id,
            "Project B"
        );

        var projectC = new Project(
            TestAuthHandler.User2Id,
            "Project C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        using var request = CreateGetRequest(
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

        var result = await response.Content
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            GetProjectsQuery.DefaultPage,
            result.Page
        );

        Assert.Equal(
            GetProjectsQuery.DefaultPageSize,
            result.PageSize
        );

        Assert.Equal(
            2,
            result.TotalCount
        );

        Assert.Equal(
            1,
            result.TotalPages
        );

        Assert.Equivalent(
            expected: new[] { projectA.Id, projectB.Id },
            actual: result.Items.Select(item => item.Id),
            strict: true
        );

        var projectAItem = Assert.Single(
            result.Items,
            item => item.Id == projectA.Id
        );

        Assert.Equal(
            projectA.Name,
            projectAItem.Name
        );

        Assert.InRange(
            projectAItem.CreatedAtUtc,
            projectA.CreatedAtUtc.AddMilliseconds(-1),
            projectA.CreatedAtUtc.AddMilliseconds(1)
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnEmptyList_WhenUserHasNoProjects()
    {
        using var request = CreateGetRequest(
            Guid.NewGuid()
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Empty(result.Items);

        Assert.Equal(
            GetProjectsQuery.DefaultPage,
            result.Page
        );

        Assert.Equal(
            GetProjectsQuery.DefaultPageSize,
            result.PageSize
        );

        Assert.Equal(
            0,
            result.TotalCount
        );

        Assert.Equal(
            1,
            result.TotalPages
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnProjectsOrderedByCreatedAtUtcDescending()
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

        var projectC = new Project(
            userId,
            "Project C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var oldestCreatedAtUtc = new DateTime(
                2026,
                8,
                20,
                10,
                0,
                0,
                DateTimeKind.Utc
            );

            var middleCreatedAtUtc = new DateTime(
                2026,
                8,
                20,
                11,
                0,
                0,
                DateTimeKind.Utc
            );

            var newestCreatedAtUtc = new DateTime(
                2026,
                8,
                20,
                12,
                0,
                0,
                DateTimeKind.Utc
            );

            var projectAUpdatedCount = await dbContext.Projects
                .Where(project => project.Id == projectA.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.CreatedAtUtc,
                        oldestCreatedAtUtc
                    ),
                    CancellationToken
                );

            var projectBUpdatedCount = await dbContext.Projects
                .Where(project => project.Id == projectB.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.CreatedAtUtc,
                        newestCreatedAtUtc
                    ),
                    CancellationToken
                );

            var projectCUpdatedCount = await dbContext.Projects
                .Where(project => project.Id == projectC.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.CreatedAtUtc,
                        middleCreatedAtUtc
                    ),
                    CancellationToken
                );

            Assert.Equal(
                1,
                projectAUpdatedCount
            );

            Assert.Equal(
                1,
                projectBUpdatedCount
            );

            Assert.Equal(
                1,
                projectCUpdatedCount
            );
        }

        using var request = CreateGetRequest(
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        var expectedIds = new[]
        {
            projectB.Id,
            projectC.Id,
            projectA.Id
        };

        var actualIds = result.Items
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(
            expectedIds,
            actualIds
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnProjectsOrderedById_WhenCreatedAtUtcIsEqual()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Tie breaker A"
        );

        var projectB = new Project(
            userId,
            "Tie breaker B"
        );

        await AddProjectsAsync(
            projectA,
            projectB
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var createdAt = new DateTime(
                2026,
                8,
                20,
                12,
                0,
                0,
                DateTimeKind.Utc
            );

            var updatedProjectsCount = await dbContext.Projects
                .Where(project =>
                    project.Id == projectA.Id
                    || project.Id == projectB.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        project => project.CreatedAtUtc,
                        createdAt
                    ),
                    CancellationToken
                );

            Assert.Equal(
                2,
                updatedProjectsCount
            );
        }

        using var request = CreateGetRequest(
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        var expectedIds = new[]
            {
                projectA.Id,
                projectB.Id
            }
            .OrderBy(id => id)
            .ToArray();

        var actualIds = result.Items
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal(
            expectedIds,
            actualIds
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateGetRequest();

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
    public async Task GetProjects_ShouldReturnUnauthorized_WhenUserIdIsEmpty()
    {
        using var request = CreateGetRequest(
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
    public async Task GetProjects_ShouldReturnRequestedPage()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Pagination A"
        );

        var projectB = new Project(
            userId,
            "Pagination B"
        );

        var projectC = new Project(
            userId,
            "Pagination C"
        );

        var projectD = new Project(
            userId,
            "Pagination D"
        );

        var projectE = new Project(
            userId,
            "Pagination E"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC,
            projectD,
            projectE
        );

        var baseDate = new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (projectA, baseDate),
            (projectB, baseDate.AddHours(1)),
            (projectC, baseDate.AddHours(2)),
            (projectD, baseDate.AddHours(3)),
            (projectE, baseDate.AddHours(4))
        );

        using var request = CreateGetRequest(
            userId,
            page: 2,
            pageSize: 2
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            2,
            result.Page
        );

        Assert.Equal(
            2,
            result.PageSize
        );

        Assert.Equal(
            5,
            result.TotalCount
        );

        Assert.Equal(
            3,
            result.TotalPages
        );

        Assert.Equal(
            new[]
            {
                projectC.Id,
                projectB.Id
            },
            result.Items
                .Select(item => item.Id)
                .ToArray()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetProjects_ShouldReturnFirstPage_WhenPageIsLessThanOne(
        int page)
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Invalid page A"
        );

        var projectB = new Project(
            userId,
            "Invalid page B"
        );

        var projectC = new Project(
            userId,
            "Invalid page C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        var baseDate = new DateTime(
            2026,
            8,
            21,
            10,
            0,
            0,
            DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (projectA, baseDate),
            (projectB, baseDate.AddHours(1)),
            (projectC, baseDate.AddHours(2))
        );

        using var request = CreateGetRequest(
            userId,
            page: page,
            pageSize: 2
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            GetProjectsQuery.DefaultPage,
            result.Page
        );

        Assert.Equal(
            2,
            result.PageSize
        );

        Assert.Equal(
            3,
            result.TotalCount
        );

        Assert.Equal(
            2,
            result.TotalPages
        );

        Assert.Equal(
            new[]
            {
                projectC.Id,
                projectB.Id
            },
            result.Items
                .Select(item => item.Id)
                .ToArray()
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnLastPage_WhenPageExceedsTotalPages()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Last page A"
        );

        var projectB = new Project(
            userId,
            "Last page B"
        );

        var projectC = new Project(
            userId,
            "Last page C"
        );

        var projectD = new Project(
            userId,
            "Last page D"
        );

        var projectE = new Project(
            userId,
            "Last page E"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC,
            projectD,
            projectE
        );

        var baseDate = new DateTime(
            2026,
            8,
            22,
            10,
            0,
            0,
            DateTimeKind.Utc
        );

        await SetCreatedAtUtcAsync(
            (projectA, baseDate),
            (projectB, baseDate.AddHours(1)),
            (projectC, baseDate.AddHours(2)),
            (projectD, baseDate.AddHours(3)),
            (projectE, baseDate.AddHours(4))
        );

        using var request = CreateGetRequest(
            userId,
            page: 999,
            pageSize: 2
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            3,
            result.Page
        );

        Assert.Equal(
            2,
            result.PageSize
        );

        Assert.Equal(
            5,
            result.TotalCount
        );

        Assert.Equal(
            3,
            result.TotalPages
        );

        var item = Assert.Single(result.Items);

        Assert.Equal(
            projectA.Id,
            item.Id
        );
    }

    [Fact]
    public async Task GetProjects_ShouldReturnAllProjects_WhenPageSizeExceedsProjectCount()
    {
        var userId = Guid.NewGuid();

        var projectA = new Project(
            userId,
            "Large page size A"
        );

        var projectB = new Project(
            userId,
            "Large page size B"
        );

        var projectC = new Project(
            userId,
            "Large page size C"
        );

        await AddProjectsAsync(
            projectA,
            projectB,
            projectC
        );

        using var request = CreateGetRequest(
            userId,
            page: GetProjectsQuery.DefaultPage,
            pageSize: 50
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
            .ReadFromJsonAsync<GetProjectsResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            GetProjectsQuery.DefaultPage,
            result.Page
        );

        Assert.Equal(
            50,
            result.PageSize
        );

        Assert.Equal(
            3,
            result.TotalCount
        );

        Assert.Equal(
            1,
            result.TotalPages
        );

        Assert.Equivalent(
            expected: new[]
            {
                projectA.Id,
                projectB.Id,
                projectC.Id
            },
            actual: result.Items.Select(item => item.Id),
            strict: true
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(101)]
    public async Task GetProjects_ShouldReturnBadRequest_WhenPageSizeIsInvalid(
        int pageSize)
    {
        using var request = CreateGetRequest(
            Guid.NewGuid(),
            pageSize: pageSize
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
                "PageSize",
                out var errors
            )
        );

        Assert.Single(errors);

        Assert.Equal(
            $"Page size must be between 1 and {GetProjectsQuery.MaxPageSize}.",
            errors[0]
        );
    }
}
