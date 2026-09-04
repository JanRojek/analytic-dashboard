using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Application.Auth;
using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class RegisterUserEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public RegisterUserEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreatePostRequest(
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

    private static HttpRequestMessage CreateRawPostRequest(
        string path,
        string json)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            path
        )
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnCreated_WhenRequestIsValid()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        const string displayName = "   User display name   ";

        const string password = "Trgjnyrsgmir!4";

        using var request = CreatePostRequest(
            email,
            displayName,
            password
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<RegisterUserResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.NotEqual(
            Guid.Empty,
            result.Id
        );

        Assert.Equal(
            email,
            result.Email
        );

        Assert.Equal(
            "User display name",
            result.DisplayName
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == result.Id,
            CancellationToken
        );

        Assert.Equal(
            result.Id,
            user.Id
        );

        Assert.Equal(
            email,
            user.Email
        );

        Assert.Equal(
            email,
            user.UserName
        );

        Assert.Equal(
            "User display name",
            user.DisplayName
        );

        Assert.Equal(
            user.CreatedAtUtc,
            result.CreatedAtUtc,
            TimeSpan.FromMilliseconds(1)
        );

        Assert.False(
            user.EmailConfirmed
        );

        Assert.NotNull(
            user.PasswordHash
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenDisplayNameIsEmpty()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var request = CreatePostRequest(
            email,
            "   ",
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid display name.",
            result.Title
        );

        Assert.Equal(
            "DisplayName cannot be empty.",
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Users.AnyAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenDisplayNameIsTooLong()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        var displayName = new string(
            'a',
            UserAccountRules.MaxDisplayNameLength + 1
        );

        using var request = CreatePostRequest(
            email,
            displayName,
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid display name.",
            result.Title
        );

        Assert.Equal(
            $"DisplayName cannot be longer than " +
            $"{UserAccountRules.MaxDisplayNameLength} characters.",
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Users.AnyAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenEmailIsInvalid()
    {
        const string email = "not-an-email";

        using var request = CreatePostRequest(
            email,
            "User display name",
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(
            result
        );

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid email.",
            result.Title
        );

        Assert.NotNull(
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Users.AnyAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenPasswordIsInvalid()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var request = CreatePostRequest(
            email,
            "User display name",
            "abc"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(
            result
        );

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid password.",
            result.Title
        );

        Assert.NotNull(
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Users.AnyAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnConflict_WhenEmailAlreadyExists()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var firstRequest = CreatePostRequest(
            email,
            "User display name 1",
            "Trgjnyrsgmir!4"
        );

        using var firstResponse = await _fixture.Client.SendAsync(
            firstRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode
        );

        using var secondRequest = CreatePostRequest(
            email,
            "User display name 2",
            "Trgjnyrsgmir!4"
        );

        using var secondResponse = await _fixture.Client.SendAsync(
            secondRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode
        );

        var result = await secondResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status409Conflict,
            result.Status
        );

        Assert.Equal(
            "Email already exists.",
            result.Title
        );

        Assert.Equal(
            "An account with this email already exists.",
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var count = await dbContext.Users.CountAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.Equal(
            1,
            count
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnConflict_WhenEmailAlreadyExistsIgnoringCase()
    {
        using var firstRequest = CreatePostRequest(
            "User@example.com",
            "User display name 1",
            "Trgjnyrsgmir!4"
        );

        using var firstResponse = await _fixture.Client.SendAsync(
            firstRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode
        );

        var result = await firstResponse.Content
            .ReadFromJsonAsync<RegisterUserResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        using var secondRequest = CreatePostRequest(
            "user@EXAMPLE.COM",
            "User display name 2",
            "Trgjnyrsgmir!4"
        );

        using var secondResponse = await _fixture.Client.SendAsync(
            secondRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            secondResponse.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var firstUser = await dbContext.Users.SingleAsync(
            user => user.Id == result.Id,
            CancellationToken
        );

        var count = await dbContext.Users.CountAsync(
            user => user.NormalizedEmail == firstUser.NormalizedEmail,
            CancellationToken
        );

        Assert.Equal(
            1,
            count
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldCreateOnlyOneUser_WhenRequestsAreConcurrent()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        using var firstRequest = CreatePostRequest(
            email,
            "User display name 1",
            "Trgjnyrsgmir!4"
        );

        using var secondRequest = CreatePostRequest(
            email,
            "User display name 2",
            "Trgjnyrsgmir!4"
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

        var statusCodes = responses
            .Select(response => response.StatusCode)
            .ToArray();

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.Created)
        );

        Assert.Equal(
            1,
            statusCodes.Count(code => code == HttpStatusCode.Conflict)
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var count = await dbContext.Users.CountAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.Equal(
            1,
            count
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnCreated_WhenDisplayNameHasMaximumLength()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        var displayName = string.Concat(
            Enumerable.Repeat(
                "🍆",
                UserAccountRules.MaxDisplayNameLength
            )
        );

        using var request = CreatePostRequest(
            email,
            displayName,
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<RegisterUserResponse>(
                CancellationToken
            );

        Assert.NotNull(result);

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Id == result.Id,
            CancellationToken
        );

        Assert.Equal(
            UserAccountRules.MaxDisplayNameLength,
            user.DisplayName.EnumerateRunes().Count()
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenDisplayNameExceedsMaximumLengthWithUnicodeCharacters()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        var displayName = string.Concat(
            Enumerable.Repeat(
                "🍆",
                UserAccountRules.MaxDisplayNameLength + 1
            )
        );

        using var request = CreatePostRequest(
            email,
            displayName,
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );

        var result = await response.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid display name.",
            result.Title
        );

        Assert.Equal(
            "DisplayName cannot be longer than 100 characters.",
            result.Detail
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Users.AnyAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenPasswordIsNull()
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["email"] = $"user-{Guid.NewGuid()}@example.com",
                ["displayName"] = "User display name",
                ["password"] = null
            }
        );

        using var request = CreateRawPostRequest(
            "/auth/register",
            json
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldReturnBadRequest_WhenPasswordIsMissing()
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["email"] = $"user-{Guid.NewGuid()}@example.com",
                ["displayName"] = "User display name"
            }
        );

        using var request = CreateRawPostRequest(
            "/auth/register",
            json
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode
        );
    }

    [Fact]
    public async Task RegisterUser_ShouldSucceed_WhenConfirmationEmailDeliveryFails()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        const string displayName = "User display name";

        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        emailSender.FailNextSend(
            new EmailDeliveryException(
                "Simulated email delivery failure."
            )
        );

        using var request = CreatePostRequest(
            email,
            displayName,
            "Trgjnyrsgmir!4"
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = await dbContext.Users.SingleAsync(
            user => user.Email == email,
            CancellationToken
        );

        Assert.Equal(
            displayName,
            user.DisplayName
        );

        Assert.False(
            user.EmailConfirmed
        );

        Assert.NotNull(
            user.PasswordHash
        );

        Assert.DoesNotContain(
            emailSender.Messages,
            message => message.RecipientEmail == email
        );

        Assert.True(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookieHeaders
            )
        );

        Assert.Contains(
            setCookieHeaders,
            header => header.StartsWith(
                "registration_session=",
                StringComparison.Ordinal
            )
        );
    }
}
