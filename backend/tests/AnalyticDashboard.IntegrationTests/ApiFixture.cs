using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgresFixture? _postgres;
    private CustomWebApplicationFactory? _factory;

    private HttpClient? _client;

    public HttpClient Client =>
        _client ?? throw new InvalidOperationException(
            "API fixture has not been initialized."
        );

    public IServiceProvider Services =>
        _factory?.Services
        ?? throw new InvalidOperationException(
            "API fixture has not been initialized."
        );

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgresFixture();
        await _postgres.InitializeAsync();

        _factory = new CustomWebApplicationFactory(_postgres.ConnectionString);
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await dbContext.Database.MigrateAsync(
            TestContext.Current.CancellationToken
        );
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
