using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Projects;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class CreateProjectEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateProjectEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateProject_ShouldReturnCreated()
    {
        var request = new CreateProjectRequest(
            "My project"
        );

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/projects",
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_ShouldReturnConflict_WhenNameAlreadyExistsIgnoringCase()
    {
        var firstRequest = new CreateProjectRequest(
            "Project"
        );

        var secondRequest = new CreateProjectRequest(
            "project"
        );

        using var firstResponse = await _fixture.Client.PostAsJsonAsync(
            "/projects",
            firstRequest,
            TestContext.Current.CancellationToken
        );

        using var secondResponse = await _fixture.Client.PostAsJsonAsync(
            "/projects",
            secondRequest,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }
}
