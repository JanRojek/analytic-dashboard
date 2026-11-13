namespace AnalyticDashboard.Application.Common.Exceptions;

public sealed class MissingReferenceException : Exception
{
    public string EntityType { get; }

    public MissingReferenceException(string entityType, Exception innerException)
        : base("A referenced resource is unavailable.", innerException)
    {
        EntityType = entityType;
    }
}
