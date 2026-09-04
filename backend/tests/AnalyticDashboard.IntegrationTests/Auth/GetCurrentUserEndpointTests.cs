using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class CurrentUserEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public CurrentUserEndpointTests(ApiFixture fixture)
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

    private static HttpRequestMessage CreateCurrentUserRequest(
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/me"
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
    public async Task CurrentUser_ShouldReturnUserDetails_WhenUserIsAuthenticated()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string displayName = "User display name";

        using var registerRequest = CreateRegisterRequest(
            email,
            displayName,
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

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Email == email,
            CancellationToken
        );

        using var currentUserRequest = CreateCurrentUserRequest(
            user.Id
        );

        using var currentUserResponse = await _fixture.Client.SendAsync(
            currentUserRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            currentUserResponse.StatusCode
        );

        var result = await currentUserResponse.Content
            .ReadFromJsonAsync<GetCurrentUserResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            user.Id,
            result.Id
        );

        Assert.Equal(
            email,
            result.Email
        );

        Assert.Equal(
            displayName,
            result.DisplayName
        );

        Assert.Equal(
            user.CreatedAtUtc,
            result.CreatedAtUtc
        );
    }

    [Fact]
    public async Task CurrentUser_ShouldReturnUnauthorized_WhenUserIsNotAuthenticated()
    {
        using var currentUserRequest = CreateCurrentUserRequest();

        using var currentUserResponse = await _fixture.Client.SendAsync(
            currentUserRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            currentUserResponse.StatusCode
        );
    }

    [Fact]
    public async Task CurrentUser_ShouldReturnUnauthorized_WhenUserNoLongerExists()
    {
        using var currentUserRequest = CreateCurrentUserRequest(
            Guid.NewGuid()
        );

        using var currentUserResponse = await _fixture.Client.SendAsync(
            currentUserRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            currentUserResponse.StatusCode
        );

        var result = await currentUserResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid authentication session.",
            result.Title
        );

        Assert.Equal(
            "The authentication session is invalid or expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task CurrentUser_ShouldReturnUserDetails_WhenAuthenticatedWithIdentityCookie()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string displayName = "User display name";
        const string password = "Trgjnyrsgmir!4";

        using var client = _fixture.CreateRealAuthClient();

        using var registerRequest = CreateRegisterRequest(
            email,
            displayName,
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

        using var currentUserRequest = CreateCurrentUserRequest();

        using var currentUserResponse = await client.SendAsync(
            currentUserRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            currentUserResponse.StatusCode
        );

        var result = await currentUserResponse.Content
            .ReadFromJsonAsync<GetCurrentUserResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            userId,
            result.Id
        );

        Assert.Equal(
            email,
            result.Email
        );

        Assert.Equal(
            displayName,
            result.DisplayName
        );
    }

    [Fact]
    public async Task GetCurrentUser_ShouldReturnUnauthorized_WhenUnauthenticatedWithRealAuthentication()
    {
        using var client = _fixture.CreateRealAuthClient(
            allowAutoRedirect: false
        );

        using var response = await client.GetAsync(
            "/auth/me",
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );
    }

    [Fact]
    public async Task GetCurrentUser_ShouldRejectAndDeleteAuthenticationSession_WhenUserNoLongerExists()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        const string password = "Trgjnyrsgmir!4";

        using var client = _fixture.CreateRealAuthClient(
            allowAutoRedirect: false
        );

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

        var confirmationMessage = Assert.Single(
            emailSender.Messages,
            message =>
                message.RecipientEmail == email &&
                message.Subject == "Confirm your email"
        );

        var linkMatch = Regex.Match(
            confirmationMessage.HtmlBody,
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

        using var currentUserBeforeDeletionResponse = await client.GetAsync(
            "/auth/me",
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            currentUserBeforeDeletionResponse.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Email == email,
            CancellationToken
        );

        dbContext.Users.Remove(
            user
        );

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        using var currentUserAfterDeletionResponse = await client.GetAsync(
            "/auth/me",
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            currentUserAfterDeletionResponse.StatusCode
        );

        Assert.True(
            currentUserAfterDeletionResponse.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookieHeaders
            )
        );

        Assert.Contains(
            setCookieHeaders,
            header =>
                header.StartsWith(
                    ".AspNetCore.Identity.Application=",
                    StringComparison.Ordinal
                ) &&
                (
                    header.Contains(
                        "expires=",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    header.Contains(
                        "max-age=0",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
        );
    }
}
