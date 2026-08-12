namespace AnalyticDashboard.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ApiSmokeTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DatabaseHealthCheck_ShouldReturnSuccess()
    {
        using var response = await _fixture.Client.GetAsync(
            "/health/db",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
