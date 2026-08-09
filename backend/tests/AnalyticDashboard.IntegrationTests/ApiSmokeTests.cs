using Microsoft.AspNetCore.Mvc.Testing;

namespace AnalyticDashboard.IntegrationTests;

public class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Factory_ShouldCreateClient()
    {
        using var client = _factory.CreateClient();

        Assert.NotNull(client);
    }
}
