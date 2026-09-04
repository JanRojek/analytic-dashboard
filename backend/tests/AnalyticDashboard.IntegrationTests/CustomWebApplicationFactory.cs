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

    public CustomWebApplicationFactory(
        string connectionString,
        bool useTestAuthentication = true)
    {
        _connectionString = connectionString;
        _useTestAuthentication = useTestAuthentication;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
