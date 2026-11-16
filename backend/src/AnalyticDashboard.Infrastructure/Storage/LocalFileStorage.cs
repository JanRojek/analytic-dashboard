using AnalyticDashboard.Application.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed partial class LocalFileStorage : IFileStorage, IStorageNamespaceCloser
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage()
        : this(Path.Combine(Directory.GetCurrentDirectory(), "storage"))
    {
    }

    public LocalFileStorage(ILogger<LocalFileStorage> logger)
        : this(Path.Combine(Directory.GetCurrentDirectory(), "storage"), logger)
    {
    }

    public LocalFileStorage(string rootPath)
        : this(rootPath, NullLogger<LocalFileStorage>.Instance)
    {
    }

    public LocalFileStorage(string rootPath, ILogger<LocalFileStorage> logger)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _logger = logger;
        if (new DriveInfo(Path.GetPathRoot(_rootPath)!).DriveType == DriveType.Network)
        {
            throw new NotSupportedException("Local storage requires a local filesystem; network shares need a verified storage protocol.");
        }
        RejectReparsePoints(_rootPath);
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        ResolvePath(storageKey);
        if (GetDatasetNamespace(storageKey) is { } datasetId)
        {
            ThrowIfNotOpen(await GetNamespaceStateAsync(datasetId, cancellationToken));
        }

        await using var staged = await StageAsync(content, cancellationToken);
        await InstallAsync(staged, storageKey, cancellationToken);
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

    public Task DeleteDirectoryAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = ResolvePath(storageKey);

        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        return Task.CompletedTask;
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

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key cannot be empty.", nameof(storageKey));
        }

        var segments = storageKey.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.Any(char.IsControl)
                || segment.Contains('~') || IsDeviceName(segment)
                || segment.IndexOfAny(['\\', ':', '<', '>', '"', '|', '?', '*', '\0']) >= 0)
            || segments[0].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')
            || string.Equals(segments[0], ProtocolDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Storage key is not a canonical relative path.", nameof(storageKey));
        }

        GetDatasetNamespace(storageKey);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, Path.Combine(segments)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(_rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Storage key points outside the storage root.", nameof(storageKey));
        }
        RejectReparsePoints(fullPath);
        return fullPath;
    }

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || name.Length == 4 && "123456789¹²³".Contains(name[3])
                && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? GetDatasetNamespace(string storageKey)
    {
        var segments = storageKey.Split('/');
        if (!string.Equals(segments[0], "datasets", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (segments[0] != "datasets" || segments.Length < 2
            || !Guid.TryParseExact(segments[1], "N", out var datasetId)
            || datasetId == Guid.Empty || datasetId.ToString("N") != segments[1])
        {
            throw new ArgumentException("Dataset storage keys must use a canonical namespace.", nameof(storageKey));
        }
        return datasetId;
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Reparse points are not supported in local storage paths.");
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
