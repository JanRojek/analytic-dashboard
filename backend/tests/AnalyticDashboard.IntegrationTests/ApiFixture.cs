using AnalyticDashboard.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgresFixture? _postgres;
    private CustomWebApplicationFactory? _factory;
    private CustomWebApplicationFactory? _realAuthFactory;

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

    public IServiceProvider RealAuthServices =>
        GetRealAuthFactory().Services;

    public async ValueTask InitializeAsync()
    {
        _postgres = new PostgresFixture();
        await _postgres.InitializeAsync();

        _factory = new CustomWebApplicationFactory(
            _postgres.ConnectionString
        );

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await dbContext.Database.MigrateAsync(
            TestContext.Current.CancellationToken
        );
    }

    public HttpClient CreateRealAuthClient(
        bool allowAutoRedirect = true)
    {
        return GetRealAuthFactory().CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
                AllowAutoRedirect = allowAutoRedirect
            }
        );
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_realAuthFactory is not null)
        {
            await _realAuthFactory.DisposeAsync();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private CustomWebApplicationFactory GetRealAuthFactory()
    {
        if (_postgres is null)
        {
            throw new InvalidOperationException(
                "API fixture has not been initialized."
            );
        }

        return _realAuthFactory ??=
            new CustomWebApplicationFactory(
                _postgres.ConnectionString,
                useTestAuthentication: false
            );
    }
}
