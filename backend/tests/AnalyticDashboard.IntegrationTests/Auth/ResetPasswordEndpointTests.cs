using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnalyticDashboard.Api.Contracts.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class ResetPasswordEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ResetPasswordEndpointTests(ApiFixture fixture)
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

    private static HttpRequestMessage CreateForgotPasswordRequest(
        string email)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/forgot-password"
        )
        {
            Content = JsonContent.Create(
                new ForgotPasswordRequest(
                    email
                )
            )
        };
    }

    private static HttpRequestMessage CreateResetPasswordRequest(
        Guid userId,
        string token,
        string newPassword)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/reset-password"
        )
        {
            Content = JsonContent.Create(
                new ResetPasswordRequest(
                    userId,
                    token,
                    newPassword
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

    private async Task<(Guid UserId, string Token)> CreateValidPasswordResetAsync(
        string email,
        string password)
    {
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

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        var resetMessage = Assert.Single(
            emailSender.Messages,
            message =>
                message.RecipientEmail == email &&
                message.Subject == "Reset your password"
        );

        var linkMatch = Regex.Match(
            resetMessage.HtmlBody,
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

        return (userId, token);
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
    public async Task ResetPassword_ShouldChangePassword_WhenTokenIsValid()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        const string oldPassword = "Trgjnyrsgmir!4";
        const string newPassword = "NewPassword!1234";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            oldPassword
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

        var confirmationMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Confirm your email"
        );

        var confirmationLinkMatch = Regex.Match(
            confirmationMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            confirmationLinkMatch.Success
        );

        var confirmationUri = new Uri(
            confirmationLinkMatch.Groups[1].Value
        );

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationUri.Query
        );

        var userId = Guid.Parse(
            confirmationQuery["userId"].ToString()
        );

        var confirmationToken = confirmationQuery["token"].ToString();

        using var confirmRequest = CreateConfirmRequest(
            userId,
            confirmationToken
        );

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var resetMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Reset your password"
        );

        var resetLinkMatch = Regex.Match(
            resetMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            resetLinkMatch.Success
        );

        var resetUri = new Uri(
            resetLinkMatch.Groups[1].Value
        );

        var resetQuery = QueryHelpers.ParseQuery(
            resetUri.Query
        );

        var resetUserId = Guid.Parse(
            resetQuery["userId"].ToString()
        );

        var resetToken = resetQuery["token"].ToString();

        Assert.Equal(
            userId,
            resetUserId
        );

        using var resetPasswordRequest = CreateResetPasswordRequest(
            resetUserId,
            resetToken,
            newPassword
        );

        using var resetPasswordResponse = await _fixture.Client.SendAsync(
            resetPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resetPasswordResponse.StatusCode
        );

        using var oldPasswordLoginRequest = CreateLoginRequest(
            email,
            oldPassword,
            rememberMe: false
        );

        using var oldPasswordLoginResponse = await _fixture.Client.SendAsync(
            oldPasswordLoginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldPasswordLoginResponse.StatusCode
        );

        using var newPasswordLoginRequest = CreateLoginRequest(
            email,
            newPassword,
            rememberMe: false
        );

        using var newPasswordLoginResponse = await _fixture.Client.SendAsync(
            newPasswordLoginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            newPasswordLoginResponse.StatusCode
        );
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnBadRequest_WhenTokenIsInvalid()
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

        var confirmationMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Confirm your email"
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

        using var resetRequest = CreateResetPasswordRequest(
            userId,
            "invalid-token",
            "NewPassword!1234"
        );

        using var resetResponse = await _fixture.Client.SendAsync(
            resetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            resetResponse.StatusCode
        );

        var result = await resetResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid reset link.",
            result.Title
        );

        Assert.Equal(
            "The password reset link is invalid or has expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnBadRequest_WhenUserDoesNotExist()
    {
        using var resetRequest = CreateResetPasswordRequest(
            Guid.NewGuid(),
            "some-token",
            "NewPassword!1234"
        );

        using var resetResponse = await _fixture.Client.SendAsync(
            resetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            resetResponse.StatusCode
        );

        var result = await resetResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.Status
        );

        Assert.Equal(
            "Invalid reset link.",
            result.Title
        );

        Assert.Equal(
            "The password reset link is invalid or has expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnBadRequest_WhenNewPasswordIsInvalid()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string oldPassword = "Trgjnyrsgmir!4";

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            oldPassword
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

        var confirmationMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Confirm your email"
        );

        var confirmationLinkMatch = Regex.Match(
            confirmationMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            confirmationLinkMatch.Success
        );

        var confirmationUri = new Uri(
            confirmationLinkMatch.Groups[1].Value
        );

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationUri.Query
        );

        var userId = Guid.Parse(
            confirmationQuery["userId"].ToString()
        );

        var confirmationToken = confirmationQuery["token"].ToString();

        using var confirmRequest = CreateConfirmRequest(
            userId,
            confirmationToken
        );

        using var confirmResponse = await _fixture.Client.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var resetMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Reset your password"
        );

        var resetLinkMatch = Regex.Match(
            resetMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            resetLinkMatch.Success
        );

        var resetUri = new Uri(
            resetLinkMatch.Groups[1].Value
        );

        var resetQuery = QueryHelpers.ParseQuery(
            resetUri.Query
        );

        var resetToken = resetQuery["token"].ToString();

        using var resetRequest = CreateResetPasswordRequest(
            userId,
            resetToken,
            "abc"
        );

        using var resetResponse = await _fixture.Client.SendAsync(
            resetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            resetResponse.StatusCode
        );

        var result = await resetResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

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

        using var loginRequest = CreateLoginRequest(
            email,
            oldPassword,
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
    }

    [Fact]
    public async Task ResetPassword_ShouldRejectToken_WhenTokenIsReused()
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

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var resetMessage = Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Reset your password"
        );

        var linkMatch = Regex.Match(
            resetMessage.HtmlBody,
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

        using var firstResetRequest = CreateResetPasswordRequest(
            userId,
            token,
            "NewPassword!1234"
        );

        using var firstResetResponse = await _fixture.Client.SendAsync(
            firstResetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResetResponse.StatusCode
        );

        using var secondResetRequest = CreateResetPasswordRequest(
            userId,
            token,
            "AnotherPassword!1234"
        );

        using var secondResetResponse = await _fixture.Client.SendAsync(
            secondResetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            secondResetResponse.StatusCode
        );

        var result = await secondResetResponse.Content
            .ReadFromJsonAsync<ProblemDetails>(
                CancellationToken
            );

        Assert.NotNull(result);

        Assert.Equal(
            "Invalid reset link.",
            result.Title
        );

        Assert.Equal(
            "The password reset link is invalid or has expired.",
            result.Detail
        );
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnBadRequest_WhenNewPasswordIsNull()
    {
        var reset = await CreateValidPasswordResetAsync(
            $"user-{Guid.NewGuid()}@example.com",
            "Trgjnyrsgmir!4"
        );

        var json = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["userId"] = reset.UserId,
                ["token"] = reset.Token,
                ["newPassword"] = null
            }
        );

        using var request = CreateRawPostRequest(
            "/auth/reset-password",
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
    public async Task ResetPassword_ShouldReturnBadRequest_WhenNewPasswordIsMissing()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        var reset = await CreateValidPasswordResetAsync(
            email,
            "Trgjnyrsgmir!4"
        );

        var json = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["userId"] = reset.UserId,
                ["token"] = reset.Token
            }
        );

        using var request = CreateRawPostRequest(
            "/auth/reset-password",
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
    public async Task ResetPassword_ShouldHandleConcurrentRequests_WhenTokenIsValid()
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

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        var resetMessage = Assert.Single(
            emailSender.Messages,
            message =>
                message.RecipientEmail == email &&
                message.Subject == "Reset your password"
        );

        var linkMatch = Regex.Match(
            resetMessage.HtmlBody,
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

        using var firstRequest = CreateResetPasswordRequest(
            userId,
            token,
            "FirstNewPassword!123"
        );

        using var secondRequest = CreateResetPasswordRequest(
            userId,
            token,
            "SecondNewPassword!123"
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

        var statusCodes = responses
            .Select(response => response.StatusCode)
            .Order()
            .ToArray();

        Assert.Equal(
            [
                HttpStatusCode.NoContent,
                HttpStatusCode.BadRequest
            ],
            statusCodes
        );
    }

    [Fact]
    public async Task ResetPassword_ShouldInvalidateExistingAuthenticationSession()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        const string oldPassword = "Trgjnyrsgmir!4";

        using var browserA = _fixture.CreateRealAuthClient();
        using var browserB = _fixture.CreateRealAuthClient();

        using var registerRequest = CreateRegisterRequest(
            email,
            "User display name",
            oldPassword
        );

        using var registerResponse = await browserA.SendAsync(
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

        var confirmationLinkMatch = Regex.Match(
            confirmationMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            confirmationLinkMatch.Success
        );

        var confirmationUri = new Uri(
            confirmationLinkMatch.Groups[1].Value
        );

        var confirmationQuery = QueryHelpers.ParseQuery(
            confirmationUri.Query
        );

        var userId = Guid.Parse(
            confirmationQuery["userId"].ToString()
        );

        var confirmationToken = confirmationQuery["token"].ToString();

        using var confirmRequest = CreateConfirmRequest(
            userId,
            confirmationToken
        );

        using var confirmResponse = await browserA.SendAsync(
            confirmRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmResponse.StatusCode
        );

        using var loginRequest = CreateLoginRequest(
            email,
            oldPassword,
            rememberMe: false
        );

        using var loginResponse = await browserA.SendAsync(
            loginRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            loginResponse.StatusCode
        );

        using var currentUserBeforeResetResponse = await browserA.GetAsync(
            "/auth/me",
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.OK,
            currentUserBeforeResetResponse.StatusCode
        );

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            email
        );

        using var forgotPasswordResponse = await browserB.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        var resetMessage = Assert.Single(
            emailSender.Messages,
            message =>
                message.RecipientEmail == email &&
                message.Subject == "Reset your password"
        );

        var resetLinkMatch = Regex.Match(
            resetMessage.HtmlBody,
            "href=\"([^\"]+)\""
        );

        Assert.True(
            resetLinkMatch.Success
        );

        var resetUri = new Uri(
            resetLinkMatch.Groups[1].Value
        );

        var resetQuery = QueryHelpers.ParseQuery(
            resetUri.Query
        );

        var resetUserId = Guid.Parse(
            resetQuery["userId"].ToString()
        );

        var resetToken = resetQuery["token"].ToString();

        using var resetRequest = CreateResetPasswordRequest(
            resetUserId,
            resetToken,
            "NewPassword!1234"
        );

        using var resetResponse = await browserB.SendAsync(
            resetRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resetResponse.StatusCode
        );

        using var currentUserAfterResetResponse = await browserA.GetAsync(
            "/auth/me",
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            currentUserAfterResetResponse.StatusCode
        );
    }
}
