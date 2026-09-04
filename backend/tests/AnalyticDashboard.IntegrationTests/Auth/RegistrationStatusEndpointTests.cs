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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class RegistrationStatusEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public RegistrationStatusEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

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

    private static HttpRequestMessage CreateRegistrationStatusRequest(
        string? cookie = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/registration-status"
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
    public async Task RegistrationStatus_ShouldReturnPending_WhenEmailIsNotConfirmed()
    {
        using var registerRequest = CreateRegisterRequest(
            $"user-{Guid.NewGuid()}@example.com",
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

        using var statusRequest = CreateRegistrationStatusRequest(
            cookie
        );

        using var statusResponse = await _fixture.Client.SendAsync(
            statusRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            statusResponse.StatusCode
        );

        var result = await statusResponse.Content
            .ReadFromJsonAsync<GetRegistrationStatusResponse>(
                JsonOptions,
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            RegistrationStatus.Pending,
            result.Status
        );
    }

    [Fact]
    public async Task RegistrationStatus_ShouldReturnConfirmed_WhenEmailIsConfirmed()
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

        var confirmationLink = linkMatch.Groups[1].Value;

        var uri = new Uri(
            confirmationLink
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

        using var statusRequest = CreateRegistrationStatusRequest(
            cookie
        );

        using var statusResponse = await _fixture.Client.SendAsync(
            statusRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            statusResponse.StatusCode
        );

        var result = await statusResponse.Content
            .ReadFromJsonAsync<GetRegistrationStatusResponse>(
                JsonOptions,
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            RegistrationStatus.Confirmed,
            result.Status
        );
    }

    [Fact]
    public async Task RegistrationStatus_ShouldReturnUnauthorized_WhenRegistrationSessionIsMissing()
    {
        using var request = CreateRegistrationStatusRequest();

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
    public async Task RegistrationStatus_ShouldReturnUnauthorized_WhenRegistrationSessionIsInvalid()
    {
        using var request = CreateRegistrationStatusRequest(
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
                out var setCookies
            )
        );

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
    }

    [Fact]
    public async Task RegistrationStatus_ShouldReturnUnauthorized_WhenUserNoLongerExists()
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

        using var statusRequest = CreateRegistrationStatusRequest(
            cookie
        );

        using var statusResponse = await _fixture.Client.SendAsync(
            statusRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            statusResponse.StatusCode
        );

        var statusResult = await statusResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(statusResult);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            statusResult.Status
        );

        Assert.Equal(
            "Invalid registration session.",
            statusResult.Title
        );

        Assert.Equal(
            "The registration session is missing, invalid, or expired.",
            statusResult.Detail
        );

        var deletedCookie = Assert.Single(
            statusResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "registration_session=",
                StringComparison.Ordinal
            )
        );

        Assert.Contains(
            "expires=",
            deletedCookie,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
