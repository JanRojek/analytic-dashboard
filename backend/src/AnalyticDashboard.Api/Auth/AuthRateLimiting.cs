using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.Api.Auth;

public static class AuthRateLimiting
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string ForgotPassword = "auth-forgot-password";
    public const string ResendConfirmation = "auth-resend-confirmation";

    public static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .Bind(configuration.GetSection("AuthRateLimiting"))
            .Validate(options => new[]
            {
                options.Login,
                options.Register,
                options.ForgotPassword,
                options.ResendConfirmation
            }.All(window => window.PermitLimit > 0 && window.WindowSeconds > 0),
                "Authentication rate limits must have positive permits and windows.")
            .ValidateOnStart();

        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>>((options, configuredLimits) =>
            {
                var limits = configuredLimits.Value;

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(
                            retryAfter.TotalSeconds
                        ).ToString(CultureInfo.InvariantCulture);
                    }

                    await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests.",
                        detail: "Please try again later."
                    ).ExecuteAsync(context.HttpContext);
                };

                AddPolicy(options, Login, limits.Login);
                AddPolicy(options, Register, limits.Register);
                AddPolicy(options, ForgotPassword, limits.ForgotPassword);
                AddPolicy(options, ResendConfirmation, limits.ResendConfirmation);
            });

        return services;
    }

    private static void AddPolicy(
        RateLimiterOptions options,
        string name,
        AuthRateLimitWindow window)
    {
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = window.PermitLimit,
                Window = TimeSpan.FromSeconds(window.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }
        ));
    }
}
