using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnalyticDashboard.Api.Contracts.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class LogoutUserEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public LogoutUserEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRegisterRequest(
        string email,
        string displayName,
        string password)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/register"
        )
        {
            Content = JsonContent.Create(
                new RegisterUserRequest(
                    email,
                    displayName,
                    password
                )
            )
        };
    }

    private static HttpRequestMessage CreateConfirmRequest(
        Guid userId,
        string token)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/confirm-email"
        )
        {
            Content = JsonContent.Create(
                new ConfirmEmailRequest(
                    userId,
                    token
                )
            )
        };
    }

    private static HttpRequestMessage CreateCompleteRegistrationRequest()
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/complete-registration"
        );
    }

    private static HttpRequestMessage CreateLoginRequest(
        string email,
        string password,
        bool rememberMe)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/login"
        )
        {
            Content = JsonContent.Create(
                new LoginUserRequest(
                    email,
                    password,
                    rememberMe
                )
            )
        };
    }

    private static HttpRequestMessage CreateLogoutRequest(
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/logout"
        );

        if (userId is not null)
        {
            request.Headers.Add(
                "X-Test-UserId",
                userId.Value.ToString()
            );
        }

        return request;
    }

    [Fact]
    public async Task Logout_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var request = CreateLogoutRequest();

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
    public async Task Logout_ShouldReturnNoContentAndDeleteAuthCookie_WhenUserIsAuthenticated()
    {
        using var request = CreateLogoutRequest(
            Guid.NewGuid()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode
        );

        var setCookies = response.Headers
            .GetValues("Set-Cookie")
            .ToArray();

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=;",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task Logout_ShouldInvalidateAuthenticationSession()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var client = _fixture.CreateRealAuthClient();

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.RealAuthServices
            .GetRequiredService<TestEmailSender>();

        var message = Assert.Single(
            emailSender.Messages,
            message => message.RecipientEmail == email
        );

        var linkMatch = Regex.Match(
            message.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            linkMatch.Success
        );

        var uri = new Uri(
            linkMatch.Groups[1].Value
        );

        var query = QueryHelpers.ParseQuery(
            uri.Query
        );

        var userId = Guid.Parse(
            query["userId"].ToString()
        );

        var token = query["token"].ToString();

        using var confirmRequest = CreateConfirmRequest(
            userId,
            token
        );

        using var confirmResponse = await client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            password,
            rememberMe: false
        );

        using var loginResponse = await client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        using var logoutRequest = CreateLogoutRequest();

        using var logoutResponse = await client.SendAsync(
            logoutRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            logoutResponse.StatusCode
        );

        using var secondLogoutRequest = CreateLogoutRequest();

        using var secondLogoutResponse = await client.SendAsync(
            secondLogoutRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            secondLogoutResponse.StatusCode
        );
    }

    [Fact]
    public async Task Logout_ShouldPreventCompletingRegistrationAfterLoginAndLogout()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var client = _fixture.CreateRealAuthClient();

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.RealAuthServices
            .GetRequiredService<TestEmailSender>();

        var message = Assert.Single(
            emailSender.Messages,
            emailMessage => emailMessage.RecipientEmail == email
        );

        var linkMatch = Regex.Match(
            message.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            linkMatch.Success
        );

        var uri = new Uri(
            linkMatch.Groups[1].Value
        );

        var query = QueryHelpers.ParseQuery(
            uri.Query
        );

        var userId = Guid.Parse(
            query["userId"].ToString()
        );

        var token = query["token"].ToString();

        using var confirmRequest = CreateConfirmRequest(
            userId,
            token
        );

        using var confirmResponse = await client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            password,
            rememberMe: false
        );

        using var loginResponse = await client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        using var logoutRequest = CreateLogoutRequest();

        using var logoutResponse = await client.SendAsync(
            logoutRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            logoutResponse.StatusCode
        );

        using var completeRegistrationRequest =
            CreateCompleteRegistrationRequest();

        using var completeRegistrationResponse = await client.SendAsync(
            completeRegistrationRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            completeRegistrationResponse.StatusCode
        );
    }
}
