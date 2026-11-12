namespace AnalyticDashboard.Api.Auth;

public sealed class AuthRateLimitOptions
{
    public AuthRateLimitWindow Login { get; set; } = new(10, 60);

    public AuthRateLimitWindow Register { get; set; } = new(3, 600);

    public AuthRateLimitWindow ForgotPassword { get; set; } = new(3, 600);

    public AuthRateLimitWindow ResendConfirmation { get; set; } = new(3, 600);
}

public sealed class AuthRateLimitWindow
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }

    public AuthRateLimitWindow() {}

    public AuthRateLimitWindow(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }
}
