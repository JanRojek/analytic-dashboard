namespace AnalyticDashboard.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgresFixture? _postgres;
    private CustomWebApplicationFactory? _factory;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgresFixture();
        await _postgres.InitializeAsync();

        _factory = new CustomWebApplicationFactory(_postgres.ConnectionString);
        Client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (_factory is not null)
            await _factory.DisposeAsync();

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}
