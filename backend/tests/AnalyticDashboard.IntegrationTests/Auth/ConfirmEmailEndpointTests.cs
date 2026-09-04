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

public sealed class ConfirmEmailEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ConfirmEmailEndpointTests(ApiFixture fixture)
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

    [Fact]
    public async Task ConfirmEmail_ShouldReturnNoContent_WhenTokenIsValid()
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

        var registerResult = await registerResponse.Content
            .ReadFromJsonAsync<RegisterUserResponse>(
                CancellationToken
            );

        Assert.NotNull(registerResult);

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

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == registerResult.Id,
            CancellationToken
        );

        Assert.True(
            user.EmailConfirmed
        );
    }

    [Fact]
    public async Task ConfirmEmail_ShouldReturnBadRequest_WhenTokenIsInvalid()
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

        var registerResult = await registerResponse.Content
            .ReadFromJsonAsync<RegisterUserResponse>(
                CancellationToken
            );

        Assert.NotNull(registerResult);

        using var confirmRequest = CreateConfirmRequest(
            registerResult.Id,
            "invalid-token"
        );

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            confirmResponse.StatusCode
        );

        var result = await confirmResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid confirmation link.",
            result.Title
        );

        Assert.Equal(
            "The email confirmation link is invalid or has expired.",
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == registerResult.Id,
            CancellationToken
        );

        Assert.False(
            user.EmailConfirmed
        );
    }

    [Fact]
    public async Task ConfirmEmail_ShouldReturnBadRequest_WhenUserDoesNotExist()
    {
        using var confirmRequest = CreateConfirmRequest(
            Guid.NewGuid(),
            "some-token"
        );

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            confirmResponse.StatusCode
        );

        var result = await confirmResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid confirmation link.",
            result.Title
        );

        Assert.Equal(
            "The email confirmation link is invalid or has expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task ConfirmEmail_ShouldReturnNoContent_WhenEmailIsAlreadyConfirmed()
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

        using var firstConfirmRequest = CreateConfirmRequest(
            userId,
            token
        );

        using var firstConfirmResponse = await _fixture.Client.SendAsync(
            firstConfirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            firstConfirmResponse.StatusCode
        );

        using var secondConfirmRequest = CreateConfirmRequest(
            userId,
            token
        );

        using var secondConfirmResponse = await _fixture.Client.SendAsync(
            secondConfirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            secondConfirmResponse.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == userId,
            CancellationToken
        );

        Assert.True(
            user.EmailConfirmed
        );
    }

    [Fact]
    public async Task ConfirmEmail_ShouldHandleConcurrentRequests_WhenTokenIsValid()
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

        using var firstRequest = CreateConfirmRequest(
            userId,
            token
        );

        using var secondRequest = CreateConfirmRequest(
            userId,
            token
        );

        var firstTask = _fixture.Client.SendAsync(
            firstRequest,
            CancellationToken
        );

        var secondTask = _fixture.Client.SendAsync(
            secondRequest,
            CancellationToken
        );

        var responses = await Task.WhenAll(
            firstTask,
            secondTask
        );

        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            secondResponse.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == userId,
            CancellationToken
        );

        Assert.True(
            user.EmailConfirmed
        );
    }
}
