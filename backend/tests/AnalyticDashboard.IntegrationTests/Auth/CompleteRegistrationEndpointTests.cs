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

public sealed class CompleteRegistrationEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public CompleteRegistrationEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateRegisterRequest(
        string email,
        string displayName,
        string password)
    {
        var request = new HttpRequestMessage(
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

        return request;
    }

    private static HttpRequestMessage CreateConfirmRequest(
        Guid userId,
        string token)
    {
        var request = new HttpRequestMessage(
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

        return request;
    }

    private static HttpRequestMessage CreateCompleteRegistrationRequest(
        string? cookie = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/complete-registration"
        );

        if (cookie is not null)
        {
            request.Headers.Add(
                "Cookie",
                cookie
            );
        }

        return request;
    }

    [Fact]
    public async Task CompleteRegistration_ShouldReturnNoContentAndIssueAuthCookie_WhenEmailIsConfirmed()
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

        var cookie = Assert.Single(
            registerResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("registration_session=")
        )
        .Split(';')[0];

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

        using var completeRequest = CreateCompleteRegistrationRequest(
            cookie
        );

        using var completeResponse = await _fixture.Client.SendAsync(
            completeRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            completeResponse.StatusCode
        );

        var setCookies = completeResponse.Headers
            .GetValues("Set-Cookie")
            .ToArray();

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal
            )
        );

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                "registration_session=;",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task CompleteRegistration_ShouldReturnConflict_WhenEmailIsNotConfirmed()
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

        var cookie = Assert.Single(
            registerResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("registration_session=")
        )
        .Split(';')[0];

        using var completeRequest = CreateCompleteRegistrationRequest(
            cookie
        );

        using var completeResponse = await _fixture.Client.SendAsync(
            completeRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            completeResponse.StatusCode
        );

        var result = await completeResponse.Content
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
            "The email address must be confirmed before registration can be completed.",
            result.Detail
        );

        if (completeResponse.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookiesEnumerable))
        {
            var setCookies = setCookiesEnumerable.ToList();

            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    "registration_session=;",
                    StringComparison.Ordinal
                )
            );

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
    public async Task CompleteRegistration_ShouldReturnUnauthorized_WhenRegistrationSessionIsMissing()
    {
        using var request = CreateCompleteRegistrationRequest();

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid registration session.",
            result.Title
        );

        Assert.Equal(
            "The registration session is missing, invalid, or expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task CompleteRegistration_ShouldReturnUnauthorized_WhenRegistrationSessionIsInvalid()
    {
        using var request = CreateCompleteRegistrationRequest(
            "registration_session=not-a-valid-protected-value"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid registration session.",
            result.Title
        );

        Assert.Equal(
            "The registration session is missing, invalid, or expired.",
            result.Detail
        );

        Assert.True(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookieHeaders
            )
        );

        var setCookies = setCookieHeaders.ToArray();

        Assert.Contains(
            setCookies,
            value =>
                value.StartsWith(
                    "registration_session=",
                    StringComparison.Ordinal
                ) &&
                (
                    value.Contains(
                        "expires=",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    value.Contains(
                        "max-age=0",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
        );

        Assert.DoesNotContain(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task CompleteRegistration_ShouldReturnUnauthorizedAndDeleteSession_WhenUserNoLongerExists()
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

        var cookie = Assert.Single(
            registerResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("registration_session=")
        )
        .Split(';')[0];

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Email == email,
            CancellationToken
        );

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(CancellationToken);

        using var completeRequest = CreateCompleteRegistrationRequest(
            cookie
        );

        using var completeResponse = await _fixture.Client.SendAsync(
            completeRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            completeResponse.StatusCode
        );

        var result = await completeResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            result.Status
        );

        Assert.Equal(
            "Invalid registration session.",
            result.Title
        );

        Assert.Equal(
            "The registration session is missing, invalid, or expired.",
            result.Detail
        );

        var setCookies = completeResponse.Headers
            .GetValues("Set-Cookie")
            .ToArray();

        Assert.Contains(
            setCookies,
            value => value.StartsWith(
                "registration_session=;",
                StringComparison.Ordinal
            )
        );

        Assert.DoesNotContain(
            setCookies,
            value => value.StartsWith(
                ".AspNetCore.Identity.Application=",
                StringComparison.Ordinal
            )
        );
    }
}
