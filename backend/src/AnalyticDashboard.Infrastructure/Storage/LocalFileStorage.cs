using AnalyticDashboard.Application.Storage;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage()
    {
        _rootPath = Path.GetFullPath(
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "storage"
            )
        );
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        var filePath = ResolvePath(storageKey);

        var directoryPath = Path.GetDirectoryName(
            filePath
        );

        if (directoryPath is not null)
        {
            Directory.CreateDirectory(
                directoryPath
            );
        }

        await using var outputStream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous
        );

        await content.CopyToAsync(
            outputStream,
            cancellationToken
        );
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = ResolvePath(storageKey);

        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous
                | FileOptions.SequentialScan
        );

        return Task.FromResult(
            stream
        );
    }

    public Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            File.Exists(
                ResolvePath(storageKey)
            )
        );
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filePath = ResolvePath(storageKey);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(
        string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException(
                "Storage key cannot be empty.",
                nameof(storageKey)
            );
        }

        var normalizedKey = storageKey.Replace(
            '/',
            Path.DirectorySeparatorChar
        );

        if (Path.IsPathRooted(normalizedKey))
        {
            throw new ArgumentException(
                "Storage key must be relative.",
                nameof(storageKey)
            );
        }

        var fullPath = Path.GetFullPath(
            Path.Combine(
                _rootPath,
                normalizedKey
            )
        );

        var rootPrefix =
            _rootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Storage key points outside the storage root.",
                nameof(storageKey)
            );
        }

        return fullPath;
    }
}
