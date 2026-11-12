using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.IntegrationTests.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnalyticDashboard.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _useTestAuthentication;
    private readonly int _authPermitLimit;

    public CustomWebApplicationFactory(
        string connectionString,
        bool useTestAuthentication = true,
        int authPermitLimit = 10000)
    {
        _connectionString = connectionString;
        _useTestAuthentication = useTestAuthentication;
        _authPermitLimit = authPermitLimit;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Functional tests share a host/IP. Rate-limit tests opt into a small limit.
        foreach (var endpoint in new[] { "Login", "Register", "ForgotPassword", "ResendConfirmation" })
        {
            builder.UseSetting(
                $"AuthRateLimiting:{endpoint}:PermitLimit",
                _authPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }

        builder.UseSetting(
            "ImportJobs:WorkerEnabled",
            "false"
        );

        builder.UseSetting(
            "StorageCleanup:WorkerEnabled",
            "false"
        );

        builder.UseSetting(
            "ConnectionStrings:Default",
            _connectionString
        );

        builder.ConfigureTestServices(services =>
        {
            if (_useTestAuthentication)
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme =
                        TestAuthHandler.AuthenticationScheme;

                    options.DefaultChallengeScheme =
                        TestAuthHandler.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.AuthenticationScheme,
                    _ => { }
                );
            }

            services.RemoveAll<IEmailSender>();

            services.AddSingleton<TestEmailSender>();

            services.AddSingleton<IEmailSender>(serviceProvider =>
                serviceProvider.GetRequiredService<TestEmailSender>()
            );
        });
    }
}
