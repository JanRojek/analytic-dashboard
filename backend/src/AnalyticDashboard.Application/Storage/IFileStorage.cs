namespace AnalyticDashboard.Application.Storage;

public interface IFileStorage
{
    Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken
    );

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken
    );

    Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken
    );

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken
    );
}
