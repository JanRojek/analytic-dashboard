using System.Diagnostics;
using System.Text.Json;

using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.Extensions.Logging;

if (args.Length != 5)
{
    return 2;
}

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var operation = args[0];
var datasetId = Guid.ParseExact(args[2], "N");
var gate = args[3];
var key = $"datasets/{datasetId:N}/{args[4]}";
var logger = new ProtocolLogger(gate);
var storage = new LocalFileStorage(args[1], logger);

try
{
    if (operation == "install")
    {
        await using var source = new MemoryStream("complete artifact"u8.ToArray());
        await using var staged = await storage.StageAsync(source, deadline.Token);
        Console.WriteLine("prepared");
        await ReadCommandAsync(deadline.Token);
        await storage.InstallAsync(staged, key, deadline.Token);
        Console.WriteLine("installed");
    }
    else if (operation == "copy")
    {
        await using var source = new GatedCopyStream();
        await storage.SaveAsync(key, source, deadline.Token);
        Console.WriteLine("installed");
    }
    else if (operation == "large")
    {
        using var process = Process.GetCurrentProcess();
        var preparing = Stopwatch.StartNew();
        await using var source = new LargeSource();
        await using var staged = await storage.StageAsync(source, deadline.Token);
        preparing.Stop();
        var installing = Stopwatch.StartNew();
        await storage.InstallAsync(staged, key, deadline.Token);
        installing.Stop();
        process.Refresh();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            SourceBytes = 1L << 30,
            InstalledBytes = new FileInfo(Path.Combine(args[1], key.Replace('/', Path.DirectorySeparatorChar))).Length,
            StagingMs = preparing.Elapsed.TotalMilliseconds,
            InstallationMs = installing.Elapsed.TotalMilliseconds,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            FileSystem = new DriveInfo(Path.GetPathRoot(args[1])!).DriveFormat
        }));
        await storage.DeleteDirectoryAsync($"datasets/{datasetId:N}", deadline.Token);
    }
    else if (operation == "close")
    {
        Console.WriteLine("prepared");
        await ReadCommandAsync(deadline.Token);
        await storage.CloseNamespaceAsync(datasetId, deadline.Token);
        Console.WriteLine("closed");
    }
    else
    {
        return 2;
    }
    return 0;
}
catch (StorageNamespaceClosedException)
{
    Console.WriteLine("rejected");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static async Task ReadCommandAsync(CancellationToken cancellationToken)
{
    if (await Console.In.ReadLineAsync(cancellationToken) != "continue")
    {
        throw new InvalidOperationException("The test process did not receive its barrier command.");
    }
}

sealed class ProtocolLogger : ILogger<LocalFileStorage>
{
    private readonly string _gate;

    public ProtocolLogger(string gate)
    {
        _gate = gate;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (message.EndsWith("lock is busy.", StringComparison.Ordinal))
        {
            Console.WriteLine("blocked");
        }
        else if (message.EndsWith(_gate == "persisted" ? "closure persisted." : "lock acquired.", StringComparison.Ordinal)
            && _gate != "none")
        {
            Console.WriteLine("locked");
            if (Console.ReadLine() != "continue")
            {
                throw new InvalidOperationException("The test process did not receive its lock barrier command.");
            }
        }
    }
}

sealed class GatedCopyStream : MemoryStream
{
    public GatedCopyStream() : base("complete artifact"u8.ToArray())
    {
    }

    public override async Task CopyToAsync(
        Stream destination,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        await destination.WriteAsync("complete "u8.ToArray(), cancellationToken);
        Console.WriteLine("copying");
        if (await Console.In.ReadLineAsync(cancellationToken) != "continue")
        {
            throw new InvalidOperationException("The test process did not receive its copy barrier command.");
        }
        await destination.WriteAsync("artifact"u8.ToArray(), cancellationToken);
    }
}

sealed class LargeSource : MemoryStream
{
    public override async Task CopyToAsync(
        Stream destination,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        for (long remaining = 1L << 30; remaining > 0; remaining -= buffer.Length)
        {
            await destination.WriteAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), cancellationToken);
        }
    }
}
