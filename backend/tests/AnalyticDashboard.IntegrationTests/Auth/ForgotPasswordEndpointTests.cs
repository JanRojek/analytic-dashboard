using System.Net;
using System.Net.Http.Json;
using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class ForgotPasswordEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ForgotPasswordEndpointTests(ApiFixture fixture)
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

    [Fact]
    public async Task ForgotPassword_ShouldSendResetEmail_WhenEmailExists()
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

        Assert.Single(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email &&
                emailMessage.Subject == "Reset your password"
        );
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnNoContent_WhenEmailDoesNotExist()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

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

        Assert.DoesNotContain(
            emailSender.Messages,
            emailMessage =>
                emailMessage.RecipientEmail == email
        );
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnNoContent_WhenEmailIsBlank()
    {
        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        var messageCountBefore =
            emailSender.Messages.Count;

        using var forgotPasswordRequest = CreateForgotPasswordRequest(
            "   "
        );

        using var forgotPasswordResponse = await _fixture.Client.SendAsync(
            forgotPasswordRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            forgotPasswordResponse.StatusCode
        );

        Assert.Equal(
            messageCountBefore,
            emailSender.Messages.Count
        );
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnNoContent_WhenEmailDeliveryFails()
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

        emailSender.FailNextSend(
            new EmailDeliveryException(
                "Simulated email delivery failure."
            )
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
    }
}
