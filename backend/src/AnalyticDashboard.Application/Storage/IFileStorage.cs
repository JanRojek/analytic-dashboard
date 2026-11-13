namespace AnalyticDashboard.Application.Storage;

public interface IFileStorage
{
    /// <summary>
    /// Creates a new file without replacing an existing one. A failed write may only clean up the file it created.
    /// </summary>
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

    Task DeleteDirectoryAsync(
        string storageKey,
        CancellationToken cancellationToken
    );

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken
    );
}
