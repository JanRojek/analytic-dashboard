using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.IntegrationTests;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationScheme = "Test";

    public static readonly Guid User1Id =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid User2Id =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public const string UserIdHeader = "X-Test-UserId";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId))
        {
            return Task.FromResult(
                AuthenticateResult.NoResult()
            );
        }

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                userId.ToString()
            )
        };

        var identity = new ClaimsIdentity(
            claims,
            AuthenticationScheme
        );

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            AuthenticationScheme
        );

        return Task.FromResult(
            AuthenticateResult.Success(ticket)
        );
    }
}
