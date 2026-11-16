namespace AnalyticDashboard.Application.Storage;

public interface IStorageNamespaceCloser
{
    Task CloseNamespaceAsync(
        Guid datasetId,
        CancellationToken cancellationToken
    );
}
