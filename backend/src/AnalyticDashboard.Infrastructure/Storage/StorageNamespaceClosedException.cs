namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class StorageNamespaceClosedException : IOException
{
    public StorageNamespaceClosedException()
        : base("The storage namespace is closed.")
    {
    }
}
