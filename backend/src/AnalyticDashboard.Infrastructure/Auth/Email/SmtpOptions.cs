namespace AnalyticDashboard.Infrastructure.Auth.Email;

public sealed class SmtpOptions
{
    public required string Host { get; set; }

    public int Port { get; set; }

    public required string Username { get; set; }

    public required string Password { get; set; }

    public required string FromEmail { get; set; }

    public required string FromName { get; set; }

    public bool UseSsl { get; set; }
}
