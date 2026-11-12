using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class LoginLockoutEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public LoginLockoutEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_ShouldCountFailures_BlockCorrectPassword_AndAllowLoginAfterExpiry()
    {
        using var client = _fixture.CreateRealAuthClient();
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"lockout-{Guid.NewGuid()}@example.com";
        const string password = "Valid-password!123";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = "Lockout test",
            EmailConfirmed = true
        };
        Assert.True((await manager.CreateAsync(user, password)).Succeeded);

        for (var attempt = 1; attempt <= manager.Options.Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/auth/login",
                new LoginUserRequest(
                    email,
                    "Incorrect-password!123",
                    false
                ),
                CancellationToken
            );

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await using var readScope = _fixture.RealAuthServices.CreateAsyncScope();
            var reader = readScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var persisted = await reader.FindByIdAsync(user.Id.ToString());
            Assert.NotNull(persisted);
            Assert.Equal(
                attempt == manager.Options.Lockout.MaxFailedAccessAttempts ? 0 : attempt,
                persisted.AccessFailedCount
            );
            Assert.Equal(
                attempt == manager.Options.Lockout.MaxFailedAccessAttempts,
                await reader.IsLockedOutAsync(persisted)
            );
        }

        using var lockedResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginUserRequest(
                email,
                password,
                true
            ),
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);
        Assert.False(lockedResponse.Headers.Contains("Set-Cookie"));
        using var meResponse = await client.GetAsync("/auth/me", CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        await using var expiryScope = _fixture.RealAuthServices.CreateAsyncScope();
        var expiryManager = expiryScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var lockedUser = await expiryManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(lockedUser);
        Assert.True((await expiryManager.SetLockoutEndDateAsync(
            lockedUser,
            DateTimeOffset.UtcNow.AddMinutes(-1)
        )).Succeeded);

        using var expiredResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginUserRequest(
                email,
                password,
                false
            ),
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, expiredResponse.StatusCode);
        Assert.Contains(
            expiredResponse.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)
        );
        using var signedInResponse = await client.GetAsync("/auth/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedInResponse.StatusCode);
    }

    [Fact]
    public async Task Login_ShouldCountFailuresAndLockOut_WhenEmailIsNotConfirmed()
    {
        using var client = _fixture.CreateRealAuthClient();
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"unconfirmed-{Guid.NewGuid()}@example.com";
        const string password = "Valid-password!123";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = "Unconfirmed lockout test",
            EmailConfirmed = false
        };
        Assert.True((await manager.CreateAsync(user, password)).Succeeded);

        for (var attempt = 1; attempt <= manager.Options.Lockout.MaxFailedAccessAttempts; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/auth/login",
                new LoginUserRequest(
                    email,
                    "Incorrect-password!123",
                    false
                ),
                CancellationToken
            );

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));

            await using var readScope = _fixture.RealAuthServices.CreateAsyncScope();
            var reader = readScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var persisted = await reader.FindByIdAsync(user.Id.ToString());
            Assert.NotNull(persisted);
            Assert.False(persisted.EmailConfirmed);
            Assert.Equal(
                attempt == manager.Options.Lockout.MaxFailedAccessAttempts ? 0 : attempt,
                persisted.AccessFailedCount
            );
            Assert.Equal(
                attempt == manager.Options.Lockout.MaxFailedAccessAttempts,
                await reader.IsLockedOutAsync(persisted)
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_ShouldRejectCorrectPasswordDuringLockout_WhenEmailIsNotConfirmed(
        bool rememberMe)
    {
        using var client = _fixture.CreateRealAuthClient();
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"locked-unconfirmed-{Guid.NewGuid()}@example.com";
        const string password = "Valid-password!123";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = "Locked unconfirmed test",
            EmailConfirmed = false
        };
        Assert.True((await manager.CreateAsync(user, password)).Succeeded);
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.True((await manager.SetLockoutEndDateAsync(user, lockoutEnd)).Succeeded);

        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginUserRequest(
                email,
                password,
                rememberMe
            ),
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var meResponse = await client.GetAsync("/auth/me", CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        await using var readScope = _fixture.RealAuthServices.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await reader.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.True(await reader.IsLockedOutAsync(persisted));
        Assert.False(persisted.EmailConfirmed);
        Assert.Equal(0, persisted.AccessFailedCount);
    }

    [Fact]
    public async Task Login_ShouldResetFailureCount_WhenPasswordIsCorrect()
    {
        using var client = _fixture.CreateRealAuthClient();
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"reset-{Guid.NewGuid()}@example.com";
        const string password = "Valid-password!123";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = "Failure reset test",
            EmailConfirmed = true
        };
        Assert.True((await manager.CreateAsync(user, password)).Succeeded);

        using var failedResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginUserRequest(
                email,
                "Incorrect-password!123",
                false
            ),
            CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);

        using var successResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginUserRequest(
                email,
                password,
                false
            ),
            CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, successResponse.StatusCode);
        await using var readScope = _fixture.RealAuthServices.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var persisted = await reader.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(persisted);
        Assert.Equal(0, persisted.AccessFailedCount);
    }
}
