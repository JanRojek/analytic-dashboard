namespace AnalyticDashboard.Infrastructure.Storage;

public sealed class LocalStagedFile : IAsyncDisposable
{
    private int _disposed;

    internal LocalStagedFile(LocalFileStorage owner, string path)
    {
        Owner = owner;
        Path = path;
    }

    internal LocalFileStorage Owner { get; }
    internal string Path { get; }

    internal void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            File.Delete(Path);
        }
        return ValueTask.CompletedTask;
    }
}
