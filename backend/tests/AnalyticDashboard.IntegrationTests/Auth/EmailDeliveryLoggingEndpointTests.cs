using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Auth;
using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Application.Auth.ForgotPassword;
using AnalyticDashboard.Application.Auth.Register;
using AnalyticDashboard.Application.Auth.ResendConfirmation;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Identity;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.IntegrationTests.Auth;

public sealed class EmailDeliveryLoggingEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public EmailDeliveryLoggingEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("register", true, 1701)]
    [InlineData("register", false, 1701)]
    [InlineData("forgot-password", true, 1702)]
    [InlineData("forgot-password", false, 1702)]
    [InlineData("resend-confirmation", true, 1703)]
    [InlineData("resend-confirmation", false, 1703)]
    public async Task EmailFailure_ShouldLogSafeWarning_AndPreservePublicResponse(
        string operation,
        bool innerException,
        int eventId)
    {
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var logs = new CapturingLoggerProvider();
        await using var baseFactory = new CustomWebApplicationFactory(
            dbContext.Database.GetConnectionString()!,
            useTestAuthentication: false
        );
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs))
        );
        using var client = factory.CreateClient();
        var email = $"logging-{Guid.NewGuid():N}@example.com";
        const string displayName = "Confidential Display Name";
        const string password = "Confidential-password!123";
        if (operation != "register")
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                DisplayName = displayName
            };
            Assert.True((await manager.CreateAsync(user, password)).Succeeded);
        }
        string[] sensitiveValues =
        [
            email,
            displayName,
            password,
            "confidential-token",
            "https://example.com/reset?token=confidential-token",
            "<p>Confidential message body</p>"
        ];
        var sensitiveMessage = string.Join(" ", sensitiveValues);
        factory.Services.GetRequiredService<TestEmailSender>().FailNextSend(new EmailDeliveryException(
            sensitiveMessage,
            innerException ? new IOException(sensitiveMessage) : null
        ));
        object body = operation == "register"
            ? new RegisterUserRequest(email, displayName, password)
            : new { email };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/auth/{operation}"
        );
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(
            operation == "register" ? HttpStatusCode.Created : HttpStatusCode.NoContent,
            response.StatusCode
        );
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(eventId, entry.EventId.Id);
        var expectedCategory = operation switch
        {
            "register" => typeof(RegisterUserHandler).FullName,
            "forgot-password" => typeof(ForgotPasswordHandler).FullName,
            "resend-confirmation" => typeof(ResendConfirmationHandler).FullName,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Assert.Equal(expectedCategory, entry.Category);
        Assert.Null(entry.Exception);
        Assert.Equal(
            innerException ? nameof(IOException) : nameof(EmailDeliveryException),
            entry.State["FailureType"]
        );
        Assert.Equal(2, entry.State.Count);
        Assert.True(entry.State.ContainsKey("{OriginalFormat}"));
        foreach (var sensitive in sensitiveValues)
        {
            Assert.DoesNotContain(sensitive, entry.Message);
            Assert.All(entry.State.Values, value => Assert.DoesNotContain(sensitive, value));
        }
        Assert.Empty(factory.Services.GetRequiredService<TestEmailSender>().Messages);

        if (operation == "register")
        {
            var result = await response.Content.ReadFromJsonAsync<RegisterUserResponse>(CancellationToken);
            Assert.NotNull(result);
            Assert.Equal(email, result.Email);
            var user = await dbContext.Users.SingleAsync(user => user.Id == result.Id, CancellationToken);
            Assert.False(user.EmailConfirmed);
        }
        else
        {
            Assert.Equal("", await response.Content.ReadAsStringAsync(CancellationToken));
            using var unknownRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/auth/{operation}"
            );
            unknownRequest.Content = JsonContent.Create(new { email = $"absent-{Guid.NewGuid():N}@example.com" });

            using var unknownResponse = await client.SendAsync(unknownRequest, CancellationToken);

            Assert.Equal(response.StatusCode, unknownResponse.StatusCode);
            Assert.Equal("", await unknownResponse.Content.ReadAsStringAsync(CancellationToken));
            Assert.Single(logs.Entries);
        }
    }

    [Theory]
    [InlineData("register")]
    [InlineData("forgot-password")]
    [InlineData("resend-confirmation")]
    public async Task SuccessfulDelivery_ShouldNotLogFailure(string operation)
    {
        await using var scope = _fixture.RealAuthServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var logs = new CapturingLoggerProvider();
        await using var baseFactory = new CustomWebApplicationFactory(
            dbContext.Database.GetConnectionString()!,
            useTestAuthentication: false
        );
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs))
        );
        using var client = factory.CreateClient();
        var email = $"success-{Guid.NewGuid():N}@example.com";
        if (operation != "register")
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                DisplayName = "Delivery success test"
            };
            Assert.True((await manager.CreateAsync(user, "Valid-password!123")).Succeeded);
        }
        object body = operation == "register"
            ? new RegisterUserRequest(email, "Delivery success test", "Valid-password!123")
            : new { email };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/auth/{operation}"
        );
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(
            operation == "register" ? HttpStatusCode.Created : HttpStatusCode.NoContent,
            response.StatusCode
        );
        Assert.Single(factory.Services.GetRequiredService<TestEmailSender>().Messages);
        Assert.Empty(logs.Entries);
    }

    private sealed record LogEntry(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, string> State
    );

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<LogEntry> _entries;

        public CapturingLogger(string category, ConcurrentQueue<LogEntry> entries)
        {
            _category = category;
            _entries = entries;
        }

        public bool IsEnabled(LogLevel logLevel) =>
            _category == typeof(RegisterUserHandler).FullName
            || _category == typeof(ForgotPasswordHandler).FullName
            || _category == typeof(ResendConfirmationHandler).FullName;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var values = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            _entries.Enqueue(new LogEntry(
                _category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                values.ToDictionary(item => item.Key, item => item.Value?.ToString() ?? "")
            ));
        }
    }
}
