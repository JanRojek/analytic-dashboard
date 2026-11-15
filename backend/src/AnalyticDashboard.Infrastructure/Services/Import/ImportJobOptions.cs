namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class ImportJobOptions
{
    public int MaxConcurrentImports { get; set; } = 2;

    public int DuckDbThreads { get; set; } = 1;

    public int DuckDbMemoryLimitMb { get; set; } = 256;

    public int DuckDbMaxTempDirectorySizeMb { get; set; } = 1024;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    public int MaxAttempts { get; set; } = 3;
}
