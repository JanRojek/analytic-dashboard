using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Application.Auth.Email;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class ResendConfirmationEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ResendConfirmationEndpointTests(ApiFixture fixture)
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

    private static HttpRequestMessage CreateResendConfirmationRequest(
        string email)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/auth/resend-confirmation"
        )
        {
            Content = JsonContent.Create(
                new ResendConfirmationRequest(
                    email
                )
            )
        };
    }

    [Fact]
    public async Task ResendConfirmation_ShouldSendNewConfirmationEmail_WhenEmailIsNotConfirmed()
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

        var messagesBeforeResend = emailSender.Messages
            .Where(message => message.RecipientEmail == email)
            .ToArray();

        Assert.Single(
            messagesBeforeResend
        );

        using var resendRequest = CreateResendConfirmationRequest(
            email
        );

        using var resendResponse = await _fixture.Client.SendAsync(
            resendRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resendResponse.StatusCode
        );

        var messagesAfterResend = emailSender.Messages
            .Where(message => message.RecipientEmail == email)
            .ToArray();

        Assert.Equal(
            2,
            messagesAfterResend.Length
        );

        var resentMessage = messagesAfterResend[1];

        var linkMatch = Regex.Match(
            resentMessage.HtmlBody,
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

        if (resendResponse.Headers.TryGetValues(
            "Set-Cookie",
            out var setCookies))
        {
            Assert.DoesNotContain(
                setCookies,
                value => value.StartsWith(
                    "registration_session=",
                    StringComparison.Ordinal
                )
            );
        }
    }

    [Fact]
    public async Task ResendConfirmation_ShouldReturnNoContent_WhenEmailDoesNotExist()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";

        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        using var resendRequest = CreateResendConfirmationRequest(
            email
        );

        using var resendResponse = await _fixture.Client.SendAsync(
            resendRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resendResponse.StatusCode
        );

        Assert.DoesNotContain(
            emailSender.Messages,
            emailMessage => emailMessage.RecipientEmail == email
        );
    }

    [Fact]
    public async Task ResendConfirmation_ShouldNotSendEmail_WhenEmailIsAlreadyConfirmed()
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

        using var resendRequest = CreateResendConfirmationRequest(
            email
        );

        using var resendResponse = await _fixture.Client.SendAsync(
            resendRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resendResponse.StatusCode
        );

        Assert.Single(
            emailSender.Messages,
            emailMessage => emailMessage.RecipientEmail == email
        );
    }

    [Fact]
    public async Task ResendConfirmation_ShouldReturnNoContent_WhenEmailIsBlank()
    {
        var emailSender = _fixture.Services
            .GetRequiredService<TestEmailSender>();

        var messageCountBefore = emailSender.Messages.Count;

        using var resendRequest = CreateResendConfirmationRequest(
            "   "
        );

        using var resendResponse = await _fixture.Client.SendAsync(
            resendRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resendResponse.StatusCode
        );

        Assert.Equal(
            messageCountBefore,
            emailSender.Messages.Count
        );
    }

    [Fact]
    public async Task ResendConfirmation_ShouldReturnNoContent_WhenEmailDeliveryFails()
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

        using var resendRequest = CreateResendConfirmationRequest(
            email
        );

        using var resendResponse = await _fixture.Client.SendAsync(
            resendRequest,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            resendResponse.StatusCode
        );
    }
}
