using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.Configuration;

namespace AnalyticDashboard.Infrastructure.Auth.Email;

public sealed class PasswordResetLinkBuilder : IPasswordResetLinkBuilder
{
    private readonly string _frontendBaseUrl;

    public PasswordResetLinkBuilder(IConfiguration configuration)
    {
        var baseUrl = configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Missing required configuration: 'Frontend:BaseUrl'."
            );
        }

        _frontendBaseUrl = baseUrl.TrimEnd('/');
    }

    public string Build(
        Guid userId,
        string token)
    {
        var encodedToken = Uri.EscapeDataString(
            token
        );

        return $"{_frontendBaseUrl}/reset-password" +
               $"?userId={userId}" +
               $"&token={encodedToken}";
    }
}
