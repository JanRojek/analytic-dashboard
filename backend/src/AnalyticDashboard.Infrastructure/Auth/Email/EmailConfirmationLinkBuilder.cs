using AnalyticDashboard.Application.Auth.Email;
using Microsoft.Extensions.Configuration;

namespace AnalyticDashboard.Infrastructure.Auth.Email;

public sealed class EmailConfirmationLinkBuilder : IEmailConfirmationLinkBuilder
{
    private readonly string _frontendBaseUrl;

    public EmailConfirmationLinkBuilder(IConfiguration configuration)
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

        return $"{_frontendBaseUrl}/confirm-email" +
               $"?userId={userId}" +
               $"&token={encodedToken}";
    }
}
