namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobOptions
{
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    public int MaxAttempts { get; set; } = 3;
}
