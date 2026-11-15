using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Threading.Channels;

using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ParallelImportWorkerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ParallelImportWorkerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Worker_ShouldRunUpToItsLimit_WithIndependentScopesAndNoPartialPublication(
        int limit)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(Enumerable.Range(0, limit + 1).Select(_ => Guid.NewGuid()).ToArray(), cancellationToken);
        var monitor = new ImportMonitor(seed);
        var clock = new ManualClock();
        await using var provider = CreateProvider(monitor, clock, limit);
        var worker = provider.GetRequiredService<ImportJobWorker>();
        try
        {
            await worker.StartAsync(cancellationToken);
            for (var index = 0; index < limit; index++)
            {
                await monitor.Jobs[seed.Jobs[index].Id].OutputSaved.Task.WaitAsync(cancellationToken);
            }

            Assert.Equal(limit, monitor.ActiveContexts.Count);
            Assert.Equal(limit, monitor.PeakActive);
            Assert.False(monitor.Jobs[seed.Jobs[limit].Id].SourceEntered.Task.IsCompleted);
            Assert.Equal(limit, monitor.ActiveContexts.Keys.Distinct().Count());
            Assert.All(monitor.ActiveContexts.Values, context =>
            {
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            });
            await using (var context = CreateContext())
            {
                Assert.Equal(limit, await context.ImportJobs.CountAsync(job => job.Status == ImportJobStatus.Running, cancellationToken));
                Assert.All(await context.DatasetVersions.AsNoTracking().ToListAsync(cancellationToken),
                    version => Assert.Equal(DatasetVersionStatus.Pending, version.Status));
                Assert.All(await context.Datasets.AsNoTracking().ToListAsync(cancellationToken),
                    dataset => Assert.Null(dataset.CurrentVersionId));
            }

            monitor.Jobs[seed.Jobs[0].Id].AllowPublish.TrySetResult();
            Assert.Equal("Completed", await monitor.Jobs[seed.Jobs[0].Id].Settled.Task.WaitAsync(cancellationToken));
            await monitor.Jobs[seed.Jobs[limit].Id].OutputSaved.Task.WaitAsync(cancellationToken);

            Assert.Equal(limit, monitor.PeakActive);
            Assert.Equal(limit, monitor.ActiveContexts.Count);
            Assert.Equal(ImportJobStatus.Completed, (await ReadJobAsync(seed.Jobs[0].Id, cancellationToken)).Status);
        }
        finally
        {
            await StopAsync(worker, cancellationToken);
            Assert.Empty(monitor.ActiveContexts);
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ParallelWorker_ShouldKeepOneRunningPerOwner_AndUseItsOtherSlotForAnotherOwner()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var owner = Guid.NewGuid();
        var seed = await SeedAsync([owner, owner, Guid.NewGuid()], cancellationToken);
        var monitor = new ImportMonitor(seed);
        await using var provider = CreateProvider(monitor, new ManualClock(), 2);
        var worker = provider.GetRequiredService<ImportJobWorker>();
        try
        {
            await worker.StartAsync(cancellationToken);
            await monitor.Jobs[seed.Jobs[0].Id].OutputSaved.Task.WaitAsync(cancellationToken);
            await monitor.Jobs[seed.Jobs[2].Id].OutputSaved.Task.WaitAsync(cancellationToken);

            Assert.False(monitor.Jobs[seed.Jobs[1].Id].SourceEntered.Task.IsCompleted);
            Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed.Jobs[1].Id, cancellationToken)).Status);
            await AssertOwnerLimitAsync(cancellationToken);

            monitor.Jobs[seed.Jobs[0].Id].AllowPublish.TrySetResult();
            await monitor.Jobs[seed.Jobs[0].Id].Settled.Task.WaitAsync(cancellationToken);
            await monitor.Jobs[seed.Jobs[1].Id].OutputSaved.Task.WaitAsync(cancellationToken);

            Assert.Equal(2, monitor.PeakActive);
            Assert.Equal(2, monitor.ActiveContexts.Count);
            await AssertOwnerLimitAsync(cancellationToken);
        }
        finally
        {
            await StopAsync(worker, cancellationToken);
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("cancelled")]
    [InlineData("sql_failure")]
    public async Task Slot_ShouldBeReusedAfterSuccessFailureOrIndividualCancellation(
        string outcome)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], cancellationToken);
        var monitor = new ImportMonitor(seed);
        var first = monitor.Jobs[seed.Jobs[0].Id];
        first.BlockRead = true;
        monitor.Jobs[seed.Jobs[1].Id].BlockRead = true;
        first.ReadFailure = outcome switch
        {
            "missing" => new FileNotFoundException("Controlled missing source."),
            "cancelled" => new OperationCanceledException(new CancellationToken(canceled: true)),
            _ => null
        };
        monitor.FailPublication = outcome == "sql_failure" ? seed.Jobs[0].Id : null;
        var clock = new ManualClock();
        await using var provider = CreateProvider(monitor, clock, 1);
        var worker = provider.GetRequiredService<ImportJobWorker>();
        try
        {
            await worker.StartAsync(cancellationToken);
            await first.SourceEntered.Task.WaitAsync(cancellationToken);
            Assert.False(monitor.Jobs[seed.Jobs[1].Id].SourceEntered.Task.IsCompleted);

            first.AllowRead.TrySetResult();
            if (outcome is "success" or "sql_failure")
            {
                await first.OutputSaved.Task.WaitAsync(cancellationToken);
                first.AllowPublish.TrySetResult();
            }
            await first.ScopeEnded.Task.WaitAsync(cancellationToken);
            if (outcome == "sql_failure")
            {
                await clock.IdleDelays.Reader.ReadAsync(cancellationToken);
                clock.Advance(TimeSpan.FromSeconds(1));
            }
            await monitor.Jobs[seed.Jobs[1].Id].SourceEntered.Task.WaitAsync(cancellationToken);

            var saved = await ReadJobAsync(seed.Jobs[0].Id, cancellationToken);
            Assert.Equal(outcome switch
            {
                "success" => ImportJobStatus.Completed,
                "missing" => ImportJobStatus.Failed,
                _ => ImportJobStatus.Running
            }, saved.Status);
            Assert.Equal(1, monitor.PeakActive);
            Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed.Jobs[2].Id, cancellationToken)).Status);
            await using var context = CreateContext();
            var version = await context.DatasetVersions.AsNoTracking().SingleAsync(
                item => item.Id == seed.Versions[0].Id,
                cancellationToken
            );
            var dataset = await context.Datasets.AsNoTracking().SingleAsync(
                item => item.Id == seed.Datasets[0].Id,
                cancellationToken
            );
            Assert.Equal(outcome == "success" ? DatasetVersionStatus.Ready
                : outcome == "missing" ? DatasetVersionStatus.Failed : DatasetVersionStatus.Pending, version.Status);
            Assert.Equal(outcome == "success" ? seed.Versions[0].Id : null, dataset.CurrentVersionId);
            if (outcome != "success")
            {
                Assert.Null(version.StorageKey);
                Assert.Null(version.RowCount);
                Assert.Null(version.ColumnCount);
            }
            Assert.Equal(0, monitor.DeleteCalls);
        }
        finally
        {
            await StopAsync(worker, cancellationToken);
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ParallelHeartbeats_ShouldUseSeparateContexts_AndFenceOnlyTheWorkerThatLostItsLease()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync([Guid.NewGuid(), Guid.NewGuid()], cancellationToken);
        var monitor = new ImportMonitor(seed);
        var clock = new ManualClock();
        await using var provider = CreateProvider(monitor, clock, 2);
        var worker = provider.GetRequiredService<ImportJobWorker>();
        try
        {
            await worker.StartAsync(cancellationToken);
            await Task.WhenAll(seed.Jobs.Select(job => monitor.Jobs[job.Id].OutputSaved.Task)).WaitAsync(cancellationToken);
            await using var context = CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() + interval '30 seconds'
                WHERE "Status" = 'Running'
                """, cancellationToken);

            clock.Advance(TimeSpan.FromSeconds(20));
            await monitor.HeartbeatUpdates.Reader.ReadAsync(cancellationToken);
            await monitor.HeartbeatUpdates.Reader.ReadAsync(cancellationToken);
            foreach (var project in seed.Projects)
            {
                await using var barrier = await new ProjectWriteTransactionFactory(context).BeginAsync(
                    project.Id,
                    cancellationToken
                );
                Assert.NotNull(barrier);
            }
            Assert.Equal(2, monitor.HeartbeatContexts.Count);
            Assert.Empty(monitor.HeartbeatContexts.Keys.Intersect(monitor.ActiveContexts.Keys));
            var serverNow = (await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
                .ToListAsync(cancellationToken)).Single();
            foreach (var job in seed.Jobs)
            {
                Assert.True((await ReadJobAsync(job.Id, cancellationToken)).LeaseExpiresAtUtc > serverNow.AddSeconds(60));
            }

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() - interval '1 second'
                WHERE "Id" = {seed.Jobs[0].Id}
                """, cancellationToken);
            var repository = CreateRepository(context);
            Assert.True(await repository.RecoverNextExpiredAsync(cancellationToken));
            var newer = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );
            Assert.Equal(2L, newer.AttemptNumber);

            clock.Advance(TimeSpan.FromSeconds(20));
            await monitor.Jobs[seed.Jobs[0].Id].ScopeEnded.Task.WaitAsync(cancellationToken);

            Assert.True(monitor.Jobs[seed.Jobs[0].Id].Cancelled);
            Assert.False(monitor.Jobs[seed.Jobs[1].Id].Cancelled);
            Assert.Equal(ImportJobStatus.Running, (await ReadJobAsync(seed.Jobs[0].Id, cancellationToken)).Status);
            Assert.Equal(2L, (await ReadJobAsync(seed.Jobs[0].Id, cancellationToken)).AttemptNumber);
            var staleKey = Assert.Single(monitor.Jobs[seed.Jobs[0].Id].SavedKeys);
            Assert.True(await Storage.ExistsAsync(staleKey, cancellationToken));
            using var sourceScope = _fixture.Services.CreateScope();
            var result = await sourceScope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>().ImportAsync(
                seed.Jobs[0].SourceStorageKey,
                $"datasets/{seed.Datasets[0].Id:N}/versions/{seed.Versions[0].Id:N}/attempts",
                cancellationToken
            );
            Assert.NotEqual(staleKey, result.StorageKey);
            Assert.Equal(
                ImportJobCompletionOutcome.Completed,
                await repository.CompleteAsync(
                    newer,
                    result.StorageKey,
                    result.RowCount,
                    result.ColumnCount,
                    cancellationToken
                )
            );

            monitor.Jobs[seed.Jobs[1].Id].AllowPublish.TrySetResult();
            await monitor.Jobs[seed.Jobs[1].Id].Settled.Task.WaitAsync(cancellationToken);

            Assert.Equal(ImportJobStatus.Completed, (await ReadJobAsync(seed.Jobs[0].Id, cancellationToken)).Status);
            Assert.Equal(ImportJobStatus.Completed, (await ReadJobAsync(seed.Jobs[1].Id, cancellationToken)).Status);
            Assert.True(await Storage.ExistsAsync(result.StorageKey, cancellationToken));
            Assert.Equal(0, monitor.DeleteCalls);
            await AssertOwnerLimitAsync(cancellationToken);
        }
        finally
        {
            await StopAsync(worker, cancellationToken);
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostShutdown_ShouldJoinActiveScopes_AndAllowSafeRecoveryOnRestart(
        bool publishOneBeforeStop)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync([Guid.NewGuid(), Guid.NewGuid()], cancellationToken);
        var monitor = new ImportMonitor(seed);
        var clock = new ManualClock();
        try
        {
            using (var host = CreateHost(monitor, clock, 2))
            {
                await host.StartAsync(cancellationToken);
                await Task.WhenAll(seed.Jobs.Select(job => monitor.Jobs[job.Id].OutputSaved.Task)).WaitAsync(cancellationToken);
                if (publishOneBeforeStop)
                {
                    monitor.Jobs[seed.Jobs[0].Id].AllowPublish.TrySetResult();
                    await monitor.Jobs[seed.Jobs[0].Id].Settled.Task.WaitAsync(cancellationToken);
                }

                await host.StopAsync(cancellationToken);

                Assert.True(host.Services.GetRequiredService<ImportJobWorker>().ExecuteTask!.IsCompleted);
                Assert.Empty(monitor.ActiveContexts);
                var heartbeatCount = monitor.HeartbeatContexts.Count;
                clock.Advance(TimeSpan.FromMinutes(1));
                Assert.Equal(heartbeatCount, monitor.HeartbeatContexts.Count);
                foreach (var job in seed.Jobs)
                {
                    Assert.True(await Storage.ExistsAsync(Assert.Single(monitor.Jobs[job.Id].SavedKeys), cancellationToken));
                }
            }

            await using (var context = CreateContext())
            {
                Assert.Equal(publishOneBeforeStop ? ImportJobStatus.Completed : ImportJobStatus.Running,
                    (await ReadJobAsync(seed.Jobs[0].Id, cancellationToken)).Status);
                Assert.Equal(ImportJobStatus.Running, (await ReadJobAsync(seed.Jobs[1].Id, cancellationToken)).Status);
                await context.Database.ExecuteSqlRawAsync("""
                    UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() - interval '1 second'
                    WHERE "Status" = 'Running'
                    """, cancellationToken);
                var repository = CreateRepository(context);
                while (await repository.RecoverNextExpiredAsync(cancellationToken))
                {
                }
            }
            monitor.AutoPublish = true;
            using (var restarted = CreateHost(monitor, new ManualClock(), 2))
            {
                await restarted.StartAsync(cancellationToken);
                await Task.WhenAll(seed.Jobs.Select(job => monitor.Jobs[job.Id].Settled.Task)).WaitAsync(cancellationToken);
                await restarted.StopAsync(cancellationToken);
            }

            Assert.Empty(monitor.ActiveContexts);
            foreach (var job in seed.Jobs)
            {
                var saved = await ReadJobAsync(job.Id, cancellationToken);
                Assert.Equal(ImportJobStatus.Completed, saved.Status);
                Assert.Equal(publishOneBeforeStop && job.Id == seed.Jobs[0].Id ? 1L : 2L, saved.AttemptNumber);
                Assert.Null(saved.LeaseExpiresAtUtc);
                foreach (var key in monitor.Jobs[job.Id].SavedKeys)
                {
                    Assert.True(await Storage.ExistsAsync(key, cancellationToken));
                }
            }
            Assert.Equal(0, monitor.DeleteCalls);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task EmptyQueue_ShouldWaitBetweenPolls_AndDisposeEmptyScopes()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = new Seed([], [], [], []);
        var monitor = new ImportMonitor(seed);
        var clock = new ManualClock();
        await using var provider = CreateProvider(monitor, clock, 2);
        var worker = provider.GetRequiredService<ImportJobWorker>();
        try
        {
            await worker.StartAsync(cancellationToken);
            await clock.IdleDelays.Reader.ReadAsync(cancellationToken);
            await clock.IdleDelays.Reader.ReadAsync(cancellationToken);

            Assert.Equal(2, monitor.Selections);
            Assert.Equal(2, monitor.DisposedScopes);
            Assert.Empty(monitor.ActiveContexts);

            clock.Advance(TimeSpan.FromSeconds(1));
            await clock.IdleDelays.Reader.ReadAsync(cancellationToken);
            await clock.IdleDelays.Reader.ReadAsync(cancellationToken);

            Assert.Equal(4, monitor.Selections);
            Assert.Equal(4, monitor.DisposedScopes);
            Assert.Empty(monitor.ActiveContexts);
        }
        finally
        {
            await StopAsync(worker, cancellationToken);
        }
    }

    private IFileStorage Storage => _fixture.Services.GetRequiredService<IFileStorage>();

    private string ConnectionString
    {
        get
        {
            using var scope = _fixture.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        }
    }

    private AppDbContext CreateContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
    }

    private static ImportJobRepository CreateRepository(AppDbContext context)
    {
        return new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            Options.Create(new ImportJobOptions()),
            NullLogger<ImportJobRepository>.Instance
        );
    }

    private ServiceProvider CreateProvider(ImportMonitor monitor, ManualClock clock, int limit)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, monitor, clock, limit);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private IHost CreateHost(ImportMonitor monitor, ManualClock clock, int limit)
    {
        return new HostBuilder()
            .UseDefaultServiceProvider(options => options.ValidateScopes = true)
            .ConfigureServices(services =>
            {
                ConfigureServices(services, monitor, clock, limit);
                services.AddHostedService(provider => provider.GetRequiredService<ImportJobWorker>());
            })
            .Build();
    }

    private void ConfigureServices(IServiceCollection services, ImportMonitor monitor, ManualClock clock, int limit)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(ConnectionString).AddInterceptors(new CommandObserver(monitor), new CommitObserver(monitor)));
        services.AddSingleton(Options.Create(new ImportJobOptions { MaxConcurrentImports = limit }));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IProjectWriteTransactionFactory, ProjectWriteTransactionFactory>();
        services.AddScoped<IImportJobRepository, ImportJobRepository>();
        services.AddScoped<IFileStorage>(provider => new ObservedStorage(
            Storage,
            provider.GetRequiredService<AppDbContext>(),
            monitor
        ));
        services.AddScoped<DuckDbCsvImporter>();
        services.AddScoped<ImportJobProcessor>();
        services.AddSingleton<ImportJobWorker>();
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        return deadline;
    }

    private static async Task StopAsync(ImportJobWorker worker, CancellationToken cancellationToken)
    {
        await worker.StopAsync(cancellationToken);
        if (worker.ExecuteTask is { } execution)
        {
            await execution.WaitAsync(cancellationToken);
        }
    }

    private async Task<Seed> SeedAsync(Guid[] owners, CancellationToken cancellationToken)
    {
        var projects = new List<Project>();
        var datasets = new List<Dataset>();
        var versions = new List<DatasetVersion>();
        var jobs = new List<ImportJob>();
        await using var context = CreateContext();
        for (var index = 0; index < owners.Length; index++)
        {
            var project = new Project(
                owners[index],
                $"Parallel project {index}"
            );
            var dataset = new Dataset(
                project.Id,
                "Parallel dataset"
            );
            var version = new DatasetVersion(
                dataset.Id,
                1,
                "source.csv"
            );
            var job = new ImportJob(
                version.Id,
                "source.csv",
                $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
            );
            context.AddRange(project, dataset, version, job);
            context.Entry(job).Property(item => item.CreatedAtUtc).CurrentValue =
                new DateTime(2026, 1, 1, 0, index, 0, DateTimeKind.Utc);
            projects.Add(project);
            datasets.Add(dataset);
            versions.Add(version);
            jobs.Add(job);
            await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());
            await Storage.SaveAsync(job.SourceStorageKey, source, cancellationToken);
        }
        await context.SaveChangesAsync(cancellationToken);
        return new Seed(projects, datasets, versions, jobs);
    }

    private async Task<ImportJob> ReadJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        return await context.ImportJobs.AsNoTracking().SingleAsync(job => job.Id == id, cancellationToken);
    }

    private async Task AssertOwnerLimitAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var owners = await (
            from job in context.ImportJobs
            join version in context.DatasetVersions on job.DatasetVersionId equals version.Id
            join dataset in context.Datasets on version.DatasetId equals dataset.Id
            join project in context.Projects on dataset.ProjectId equals project.Id
            where job.Status == ImportJobStatus.Running
            select project.OwnerId
        ).ToListAsync(cancellationToken);
        Assert.All(owners.GroupBy(owner => owner), group => Assert.Single(group));
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        var ids = seed.Projects.Select(project => project.Id).ToArray();
        await context.Projects.Where(project => ids.Contains(project.Id)).ExecuteDeleteAsync(CancellationToken.None);
        foreach (var dataset in seed.Datasets)
        {
            await Storage.DeleteDirectoryAsync($"datasets/{dataset.Id:N}", CancellationToken.None);
        }
    }

    private sealed record Seed(
        IReadOnlyList<Project> Projects,
        IReadOnlyList<Dataset> Datasets,
        IReadOnlyList<DatasetVersion> Versions,
        IReadOnlyList<ImportJob> Jobs
    );

    private sealed class JobObservation
    {
        public bool BlockRead { get; set; }
        public Exception? ReadFailure { get; set; }
        public bool Cancelled { get; set; }
        public TaskCompletionSource SourceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OutputSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ScopeEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> SavedKeys { get; } = new();
    }

    private sealed class ImportMonitor
    {
        private readonly IReadOnlyDictionary<string, Guid> _sourceJobs;
        private int _active;
        private int _peak;
        private int _selections;
        private int _disposedScopes;
        private int _deleteCalls;
        public Dictionary<Guid, JobObservation> Jobs { get; }
        public ConcurrentDictionary<Guid, AppDbContext> ActiveContexts { get; } = new();
        public ConcurrentDictionary<Guid, Guid> ContextJobs { get; } = new();
        public ConcurrentDictionary<Guid, string> TerminalContexts { get; } = new();
        public ConcurrentDictionary<Guid, byte> HeartbeatContexts { get; } = new();
        public Channel<bool> HeartbeatUpdates { get; } = Channel.CreateUnbounded<bool>();
        public Guid? FailPublication { get; set; }
        public bool AutoPublish { get; set; }
        public int PeakActive => Volatile.Read(ref _peak);
        public int Selections => Volatile.Read(ref _selections);
        public int DisposedScopes => Volatile.Read(ref _disposedScopes);
        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public ImportMonitor(Seed seed)
        {
            _sourceJobs = seed.Jobs.ToDictionary(job => job.SourceStorageKey, job => job.Id);
            Jobs = seed.Jobs.ToDictionary(job => job.Id, _ => new JobObservation());
        }

        public Guid Enter(string sourceKey, AppDbContext context)
        {
            var jobId = _sourceJobs[sourceKey];
            var contextId = context.ContextId.InstanceId;
            ContextJobs[contextId] = jobId;
            ActiveContexts[contextId] = context;
            var active = Interlocked.Increment(ref _active);
            int oldPeak;
            do
            {
                oldPeak = Volatile.Read(ref _peak);
            }
            while (active > oldPeak && Interlocked.CompareExchange(ref _peak, active, oldPeak) != oldPeak);
            Jobs[jobId].SourceEntered.TrySetResult();
            return jobId;
        }

        public void End(AppDbContext context, Guid? jobId)
        {
            Interlocked.Increment(ref _disposedScopes);
            if (jobId is { } id)
            {
                ActiveContexts.TryRemove(context.ContextId.InstanceId, out _);
                Interlocked.Decrement(ref _active);
                Jobs[id].ScopeEnded.TrySetResult();
            }
        }

        public void Selected() => Interlocked.Increment(ref _selections);
        public void Deleted() => Interlocked.Increment(ref _deleteCalls);
    }

    private sealed class ObservedStorage : IFileStorage, IAsyncDisposable
    {
        private readonly IFileStorage _inner;
        private readonly AppDbContext _context;
        private readonly ImportMonitor _monitor;
        private Guid? _jobId;

        public ObservedStorage(IFileStorage inner, AppDbContext context, ImportMonitor monitor)
        {
            _inner = inner;
            _context = context;
            _monitor = monitor;
        }

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            _jobId = _monitor.Enter(storageKey, _context);
            var job = _monitor.Jobs[_jobId.Value];
            try
            {
                if (job.BlockRead)
                {
                    await job.AllowRead.Task.WaitAsync(cancellationToken);
                }
                if (job.ReadFailure is { } failure)
                {
                    throw failure;
                }
                return await _inner.OpenReadAsync(storageKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                job.Cancelled = true;
                throw;
            }
        }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(storageKey, content, cancellationToken);
            var job = _monitor.Jobs[_jobId!.Value];
            job.SavedKeys.Enqueue(storageKey);
            job.OutputSaved.TrySetResult();
            try
            {
                if (!_monitor.AutoPublish)
                {
                    await job.AllowPublish.Task.WaitAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                job.Cancelled = true;
                throw;
            }
        }

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            _monitor.Deleted();
            return _inner.DeleteAsync(storageKey, cancellationToken);
        }

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken)
        {
            _monitor.Deleted();
            return _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            _monitor.End(_context, _jobId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CommandObserver : DbCommandInterceptor
    {
        private readonly ImportMonitor _monitor;

        public CommandObserver(ImportMonitor monitor)
        {
            _monitor = monitor;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (result.FieldCount == 1 && result.GetName(0) == "OwnerId")
            {
                _monitor.Selected();
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var contextId = eventData.Context!.ContextId.InstanceId;
            if (command.CommandText.Contains("UPDATE import_jobs", StringComparison.Ordinal))
            {
                if (command.CommandText.Contains("SET \"LeaseExpiresAtUtc\"", StringComparison.Ordinal)
                    && !command.CommandText.Contains("LEAST(", StringComparison.Ordinal))
                {
                    _monitor.HeartbeatContexts[contextId] = 0;
                    _monitor.HeartbeatUpdates.Writer.TryWrite(result == 1);
                }
                var terminal = command.Parameters.Cast<DbParameter>()
                    .Select(parameter => parameter.Value).OfType<string>()
                    .FirstOrDefault(value => value is "Completed" or "Failed");
                if (terminal is not null && _monitor.ContextJobs.TryGetValue(contextId, out var jobId))
                {
                    if (terminal == "Completed" && _monitor.FailPublication == jobId)
                    {
                        throw new IOException("Controlled failure after the final UPDATE, before commit.");
                    }
                    _monitor.TerminalContexts[contextId] = terminal;
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitObserver : DbTransactionInterceptor
    {
        private readonly ImportMonitor _monitor;

        public CommitObserver(ImportMonitor monitor)
        {
            _monitor = monitor;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var contextId = eventData.Context!.ContextId.InstanceId;
            if (_monitor.TerminalContexts.TryRemove(contextId, out var terminal)
                && _monitor.ContextJobs.TryGetValue(contextId, out var jobId))
            {
                _monitor.Jobs[jobId].Settled.TrySetResult(terminal);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object _syncRoot = new();
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;
        public Channel<bool> IdleDelays { get; } = Channel.CreateUnbounded<bool>();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_syncRoot)
            {
                _timers.Add(timer);
            }
            if (dueTime == TimeSpan.FromSeconds(1) && period == Timeout.InfiniteTimeSpan)
            {
                IdleDelays.Writer.TryWrite(true);
            }
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_syncRoot)
            {
                _ticks += elapsed.Ticks;
                foreach (var timer in _timers)
                {
                    if (timer.Due <= _ticks)
                    {
                        callbacks.Add((timer.Callback, timer.State));
                        timer.Due = timer.Period > 0 ? _ticks + timer.Period : long.MaxValue;
                    }
                }
            }
            foreach (var callback in callbacks)
            {
                callback.Callback(callback.State);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualClock _clock;
            public TimerCallback Callback { get; }
            public object? State { get; }
            public long Due { get; set; } = long.MaxValue;
            public long Period { get; private set; }

            public ManualTimer(ManualClock clock, TimerCallback callback, object? state)
            {
                _clock = clock;
                Callback = callback;
                State = state;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_clock._syncRoot)
                {
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _clock._ticks + dueTime.Ticks;
                    Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_clock._syncRoot)
                {
                    Due = long.MaxValue;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
