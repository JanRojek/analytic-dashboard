namespace AnalyticDashboard.Domain.Entities;

public sealed class InvalidEntityNameException(string entityType, string message)
    : ArgumentException(message)
{
    public string EntityType { get; } = entityType;
}
