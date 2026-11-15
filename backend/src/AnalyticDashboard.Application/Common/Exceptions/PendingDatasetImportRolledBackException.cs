namespace AnalyticDashboard.Application.Common.Exceptions;

public sealed class PendingDatasetImportRolledBackException : Exception
{
    public PendingDatasetImportRolledBackException(Exception innerException)
        : base("Pending dataset import creation was rolled back.", innerException) {}
}
