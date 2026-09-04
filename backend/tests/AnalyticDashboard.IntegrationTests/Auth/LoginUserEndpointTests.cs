using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnalyticDashboard.Api.Contracts.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class LoginUserEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public LoginUserEndpointTests(ApiFixture fixture)
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

    [Fact]
    public async Task Login_ShouldReturnNoContentAndIssueAuthCookie_WhenCredentialsAreValid()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.Services
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

        using var confirmResponse = await _fixture.Client.SendAsync(
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

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        var setCookies = loginResponse.Headers
            .GetValues("Set-Cookie")
            .ToArray();

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenPasswordIsIncorrect()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            "Trgjnyrsgmir!4"
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.Services
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

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            "oingo boingo hot dog",
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            loginResponse.StatusCode
        );

        var result = await loginResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid credentials.",
            result.Title
        );

        Assert.Equal(
            "The email or password is incorrect.",
            result.Detail
        );

        if (loginResponse.Headers.TryGetValues(
            "Set-Cookie",
            out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenEmailDoesNotExist()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var loginRequest = CreateLoginRequest(
            email,
            "Trgjnyrsgmir!4",
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            loginResponse.StatusCode
        );

        var result = await loginResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid credentials.",
            result.Title
        );

        Assert.Equal(
            "The email or password is incorrect.",
            result.Detail
        );

        if (loginResponse.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task Login_ShouldReturnConflict_WhenEmailIsNotConfirmed()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            password,
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            loginResponse.StatusCode
        );

        var result = await loginResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            result.Status
        );

        Assert.Equal(
            "Email not confirmed.",
            result.Title
        );

        Assert.Equal(
            "The email address must be confirmed before signing in.",
            result.Detail
        );

        if (loginResponse.Headers.TryGetValues(
            "Set-Cookie",
            out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenEmailIsNotConfirmedAndPasswordIsIncorrect()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            "Trgjnyrsgmir!4"
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            "oingo boingo hot dog",
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            loginResponse.StatusCode
        );

        var result = await loginResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid credentials.",
            result.Title
        );

        Assert.Equal(
            "The email or password is incorrect.",
            result.Detail
        );

        if (loginResponse.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenEmailOrPasswordIsBlank()
    {
        using var loginRequest = CreateLoginRequest(
            "   ",
            "Trgjnyrsgmir!4",
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            loginResponse.StatusCode
        );

        var result = await loginResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid credentials.",
            result.Title
        );

        Assert.Equal(
            "The email or password is incorrect.",
            result.Detail
        );

        if (loginResponse.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task Login_ShouldIssueSessionCookie_WhenRememberMeIsFalse()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.Services
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

        using var confirmResponse = await _fixture.Client.SendAsync(
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

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        var cookie = Assert.Single(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=")
        );

        Assert.False(
            cookie.Contains(
                "expires=",
                StringComparison.OrdinalIgnoreCase
            )
        );

        Assert.False(
            cookie.Contains(
                "max-age=",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    [Fact]
    public async Task Login_ShouldIssuePersistentCookie_WhenRememberMeIsTrue()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.Services
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

        using var confirmResponse = await _fixture.Client.SendAsync(
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
            rememberMe: true
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        var cookie = Assert.Single(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=")
        );

        Assert.True(
            cookie.Contains(
                "expires=",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    [Fact]
    public async Task Login_ShouldReturnNoContent_WhenEmailDiffersOnlyByCase()
    {
        var email = $"User-{Guid.NewGuid()}@Example.com";
        const string password = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            password
        );

        using var registerResponse = await _fixture.Client.SendAsync(
            registerRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode
        );

        var emailSender = _fixture.Services
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

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email.ToLowerInvariant(),
            password,
            rememberMe: false
        );

        using var loginResponse = await _fixture.Client.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        var setCookies = loginResponse.Headers
            .GetValues("Set-Cookie")
            .ToArray();

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal
            )
        );
    }
}
