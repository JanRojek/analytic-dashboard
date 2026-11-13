using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Dashboards;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Dashboards;

public sealed class CreateDashboardEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public CreateDashboardEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Null")]
    [InlineData("Empty")]
    [InlineData("Whitespace")]
    [InlineData("TooLong")]
    [InlineData("TooLongUnicode")]
    public async Task CreateDashboard_ShouldRejectInvalidName_WithoutSaving(string scenario)
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Dashboard validation project");
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken);

        var name = scenario switch
        {
            "Null" => null,
            "Empty" => "",
            "Whitespace" => " \t\r\n\u2003 ",
            "TooLong" => new string('a', Dashboard.MaxNameLength + 1),
            "TooLongUnicode" => string.Concat(Enumerable.Repeat("🚀", Dashboard.MaxNameLength + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{project.Id}/dashboards");

        request.Content = JsonContent.Create(new CreateDashboardRequest(name!));
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal(
            scenario is "TooLong" or "TooLongUnicode"
                ? $"Dashboard name cannot be longer than {Dashboard.MaxNameLength} characters."
                : "Dashboard name cannot be empty.",
            Assert.Single(problem.Errors["Name"])
        );
        Assert.False(await dbContext.Dashboards.AnyAsync(
            dashboard => dashboard.ProjectId == project.Id,
            CancellationToken
        ));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateDashboard_ShouldTrimAndAcceptNameAtRuneLimit(bool useUnicode)
    {
        var userId = Guid.NewGuid();
        var project = new Project(userId, "Dashboard valid name project");
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken);
        var name = string.Concat(Enumerable.Repeat(useUnicode ? "🚀" : "a", Dashboard.MaxNameLength));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{project.Id}/dashboards");

        request.Content = JsonContent.Create(new CreateDashboardRequest($" \u2003{name}\t "));
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateDashboardResponse>(CancellationToken);
        Assert.NotNull(result);
        Assert.Equal($"/projects/{project.Id}/dashboards/{result.Id}", response.Headers.Location?.ToString());
        var dashboard = await dbContext.Dashboards.SingleAsync(
            dashboard => dashboard.Id == result.Id,
            CancellationToken
        );
        Assert.Equal(name, dashboard.Name);
        Assert.Equal(project.Id, dashboard.ProjectId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateDashboard_ShouldPreserveNotFound_ForMissingOrForeignProject(bool foreignProject)
    {
        var project = new Project(Guid.NewGuid(), "Another owner's project");
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (foreignProject)
        {
            dbContext.Projects.Add(project);
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{project.Id}/dashboards");

        request.Content = JsonContent.Create(new CreateDashboardRequest(""));
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());

        using var response = await _fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await dbContext.Dashboards.AnyAsync(
            dashboard => dashboard.ProjectId == project.Id,
            CancellationToken
        ));
    }
}
