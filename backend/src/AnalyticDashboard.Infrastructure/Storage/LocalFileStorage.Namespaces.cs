using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Storage;

public sealed partial class LocalFileStorage
{
    private const string ProtocolDirectory = ".aperture-storage";
    private static readonly byte[] OpenState = "AP_NS_O1"u8.ToArray();
    private static readonly byte[] ClosedState = "AP_NS_C1"u8.ToArray();

    public async Task<LocalStagedFile> StageAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(_rootPath, ProtocolDirectory, "staging");
        CreateStorageDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        try
        {
            await using (output)
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            return new LocalStagedFile(this, path);
        }
        catch (Exception writeException)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Storage staging failed and cleanup of its partial file also failed.",
                    writeException,
                    cleanupException
                );
            }
            throw;
        }
    }

    public async Task InstallAsync(
        LocalStagedFile stagedFile,
        string storageKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagedFile);
        stagedFile.EnsureAvailable();
        if (!ReferenceEquals(stagedFile.Owner, this))
        {
            throw new ArgumentException("The staged file belongs to another storage instance.", nameof(stagedFile));
        }

        var target = ResolvePath(storageKey);
        var datasetId = GetDatasetNamespace(storageKey);
        if (datasetId.HasValue && storageKey.Split('/').Length < 3)
        {
            throw new ArgumentException("A file key must be inside the dataset namespace.", nameof(storageKey));
        }
        await using var guard = datasetId is { } id
            ? await AcquireNamespaceAsync(id, create: true, cancellationToken)
            : null;

        if (guard is not null)
        {
            // Unix creation and flock are separate; a closer may have written before the creator acquired its lock.
            if (guard.Created && guard.Stream.Length == 0)
            {
                WriteState(guard.Stream, OpenState);
            }
            ThrowIfNotOpen(ReadState(guard.Stream));
            _logger.LogDebug("Local storage namespace {NamespaceId} installation lock acquired.", datasetId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        CreateStorageDirectory(Path.GetDirectoryName(target)!);
        RejectReparsePoints(stagedFile.Path);
        AtomicFileInstall.Move(stagedFile.Path, target);
        _logger.LogDebug("Local storage namespace {NamespaceId} file installation completed.", datasetId);
    }

    public async Task CloseNamespaceAsync(Guid datasetId, CancellationToken cancellationToken)
    {
        await using var guard = await AcquireNamespaceAsync(datasetId, create: true, cancellationToken);
        _logger.LogDebug("Local storage namespace {NamespaceId} closure lock acquired.", datasetId);
        // Never restore Open after an uncertain write; an incomplete marker also blocks installation.
        WriteClosedState(guard!.Stream);
        _logger.LogDebug("Local storage namespace {NamespaceId} closure persisted.", datasetId);
    }

    public async Task<LocalStorageNamespaceState> GetNamespaceStateAsync(
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        await using var guard = await AcquireNamespaceAsync(datasetId, create: false, cancellationToken);
        return guard is null ? LocalStorageNamespaceState.Uninitialized : ReadState(guard.Stream);
    }

    private async Task<NamespaceGuard?> AcquireNamespaceAsync(
        Guid datasetId,
        bool create,
        CancellationToken cancellationToken)
    {
        if (datasetId == Guid.Empty)
        {
            throw new ArgumentException("Dataset namespace cannot be empty.", nameof(datasetId));
        }
        var directory = Path.Combine(_rootPath, ProtocolDirectory, "namespaces");
        if (create)
        {
            CreateStorageDirectory(directory);
        }
        var path = Path.Combine(directory, datasetId.ToString("N"));
        var reportedBusy = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoints(path);
            try
            {
                if (create)
                {
                    try
                    {
                        return new NamespaceGuard(OpenControl(path, FileMode.CreateNew), created: true);
                    }
                    catch (IOException exception) when (IsAlreadyExists(exception))
                    {
                    }
                }
                return new NamespaceGuard(OpenControl(path, FileMode.Open), created: false);
            }
            catch (FileNotFoundException) when (!create)
            {
                return null;
            }
            catch (DirectoryNotFoundException) when (!create)
            {
                return null;
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (!reportedBusy)
                {
                    _logger.LogDebug("Local storage namespace {NamespaceId} lock is busy.", datasetId);
                    reportedBusy = true;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }
        }
    }

    private static FileStream OpenControl(string path, FileMode mode)
    {
        var stream = new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        try
        {
            AtomicFileInstall.RequireExclusiveLock(stream);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool IsAlreadyExists(IOException exception)
    {
        var code = exception.HResult & 0xffff;
        return code is 80 or 183 || !OperatingSystem.IsWindows() && code == 17;
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xffff;
        return code is 32 or 33 || !OperatingSystem.IsWindows() && code == 11;
    }

    private static LocalStorageNamespaceState ReadState(FileStream stream)
    {
        if (stream.Length == OpenState.Length + ClosedState.Length)
        {
            Span<byte> closed = stackalloc byte[ClosedState.Length];
            stream.Position = OpenState.Length;
            stream.ReadExactly(closed);
            return closed.SequenceEqual(ClosedState)
                ? LocalStorageNamespaceState.Closed
                : LocalStorageNamespaceState.Unknown;
        }
        if (stream.Length != OpenState.Length)
        {
            return LocalStorageNamespaceState.Unknown;
        }
        Span<byte> state = stackalloc byte[OpenState.Length];
        stream.Position = 0;
        stream.ReadExactly(state);
        if (state.SequenceEqual(OpenState))
        {
            return LocalStorageNamespaceState.Open;
        }
        return LocalStorageNamespaceState.Unknown;
    }

    private static void WriteClosedState(FileStream stream)
    {
        // Appending makes every partial close marker different from a valid Open record.
        stream.Position = OpenState.Length;
        stream.Write(ClosedState);
        stream.SetLength(OpenState.Length + ClosedState.Length);
        stream.Flush(flushToDisk: true);
        AtomicFileInstall.FlushDirectory(Path.GetDirectoryName(stream.Name)!);
    }

    private static void WriteState(FileStream stream, byte[] state)
    {
        stream.Position = 0;
        stream.Write(state);
        stream.SetLength(state.Length);
        stream.Flush(flushToDisk: true);
        AtomicFileInstall.FlushDirectory(Path.GetDirectoryName(stream.Name)!);
    }

    private static void ThrowIfNotOpen(LocalStorageNamespaceState state)
    {
        if (state == LocalStorageNamespaceState.Closed)
        {
            throw new StorageNamespaceClosedException();
        }
        if (state == LocalStorageNamespaceState.Unknown)
        {
            throw new IOException("The storage namespace state is unknown; installation is not permitted.");
        }
    }

    private void CreateStorageDirectory(string path)
    {
        RejectReparsePoints(path);
        Directory.CreateDirectory(path);
        RejectReparsePoints(path);
        if (OperatingSystem.IsLinux())
        {
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                AtomicFileInstall.FlushDirectory(current);
                if (current == _rootPath)
                {
                    var parent = Path.GetDirectoryName(current);
                    if (parent is not null)
                    {
                        AtomicFileInstall.FlushDirectory(parent);
                    }
                    break;
                }
            }
        }
    }

    private sealed class NamespaceGuard : IAsyncDisposable
    {
        public NamespaceGuard(FileStream stream, bool created)
        {
            Stream = stream;
            Created = created;
        }

        public FileStream Stream { get; }
        public bool Created { get; }
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}
