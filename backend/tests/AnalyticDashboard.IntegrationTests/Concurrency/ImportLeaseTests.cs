using System.Data.Common;

using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;

using DuckDB.NET.Data;
using DuckDB.NET.Native;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ImportLeaseTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ImportLeaseTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClaimAndHeartbeat_ShouldUsePostgresTime_AndPreserveActiveWorkDuringRecovery()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var before = await ServerNowAsync(context, cancellationToken);

            var attempt = Assert.IsType<ImportJobWorkItem>(
                await repository.ClaimNextPendingAsync(cancellationToken)
            );

            var after = await ServerNowAsync(context, cancellationToken);
            Assert.NotNull(attempt.Job.LeaseExpiresAtUtc);
            Assert.InRange(
                attempt.Job.LeaseExpiresAtUtc.Value,
                before.AddMinutes(2),
                after.AddMinutes(2)
            );
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() + interval '30 seconds'
                WHERE "Id" = {seed.Job.Id}
                """, cancellationToken);
            var previousLease = (await ReadJobAsync(seed, cancellationToken)).LeaseExpiresAtUtc;
            before = await ServerNowAsync(context, cancellationToken);

            Assert.True(await repository.TryRenewLeaseAsync(attempt, cancellationToken));

            var renewed = await ReadJobAsync(seed, cancellationToken);
            Assert.True(renewed.LeaseExpiresAtUtc > previousLease);
            Assert.InRange(
                renewed.LeaseExpiresAtUtc!.Value,
                before.AddMinutes(2),
                (await ServerNowAsync(context, cancellationToken)).AddMinutes(2)
            );
            var recovery = _fixture.Services.GetRequiredService<ImportJobRecoveryService>();

            await recovery.RecoverExpiredAsync(cancellationToken);
            await recovery.RecoverExpiredAsync(cancellationToken);

            AssertJobEqual(renewed, await ReadJobAsync(seed, cancellationToken));
            Assert.Null(context.Database.CurrentTransaction);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Recovery_ShouldRequeueExpiredWork_AndFenceTheOldAttempt()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var oldContext = CreateContext();
            await using var nextContext = CreateContext();
            var oldRepository = CreateRepository(oldContext);
            var old = Assert.IsType<ImportJobWorkItem>(
                await oldRepository.ClaimNextPendingAsync(cancellationToken)
            );
            var oldResult = await CreateResultAsync(seed, cancellationToken);
            await ExpireAsync(seed, cancellationToken);

            Assert.False(await oldRepository.TryRenewLeaseAsync(old, cancellationToken));
            Assert.Equal(
                ImportJobCompletionOutcome.OwnershipLost,
                await oldRepository.CompleteAsync(
                    old,
                    oldResult.StorageKey,
                    oldResult.RowCount,
                    oldResult.ColumnCount,
                    cancellationToken
                )
            );
            Assert.False(await oldRepository.TryFailAsync(old, "Too late", cancellationToken));
            Assert.True(await CreateRepository(nextContext).RecoverNextExpiredAsync(cancellationToken));

            var pending = await ReadJobAsync(seed, cancellationToken);
            Assert.Equal(ImportJobStatus.Pending, pending.Status);
            Assert.Equal(1L, pending.AttemptNumber);
            Assert.Null(pending.LeaseExpiresAtUtc);
            Assert.Null(pending.StartedAtUtc);
            Assert.Null(pending.CompletedAtUtc);
            Assert.Equal(DatasetVersionStatus.Pending, (await ReadVersionAsync(seed, cancellationToken)).Status);

            var nextRepository = CreateRepository(nextContext);
            var next = Assert.IsType<ImportJobWorkItem>(
                await nextRepository.ClaimNextPendingAsync(cancellationToken)
            );
            Assert.Equal(2L, next.AttemptNumber);
            var before = await ReadJobAsync(seed, cancellationToken);

            Assert.False(await oldRepository.TryRenewLeaseAsync(old, cancellationToken));
            Assert.False(await oldRepository.TryFailAsync(old, "Stale failure", cancellationToken));
            AssertJobEqual(before, await ReadJobAsync(seed, cancellationToken));

            var nextResult = await CreateResultAsync(seed, cancellationToken);
            Assert.Equal(
                ImportJobCompletionOutcome.Completed,
                await nextRepository.CompleteAsync(
                    next,
                    nextResult.StorageKey,
                    nextResult.RowCount,
                    nextResult.ColumnCount,
                    cancellationToken
                )
            );
            Assert.Equal(
                ImportJobCompletionOutcome.OwnershipLost,
                await oldRepository.CompleteAsync(
                    old,
                    oldResult.StorageKey,
                    oldResult.RowCount,
                    oldResult.ColumnCount,
                    cancellationToken
                )
            );
            Assert.Equal(nextResult.StorageKey, (await ReadVersionAsync(seed, cancellationToken)).StorageKey);
            Assert.True(await Storage.ExistsAsync(oldResult.StorageKey, cancellationToken));
            Assert.True(await Storage.ExistsAsync(nextResult.StorageKey, cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task Recovery_ShouldStopAtTheAttemptLimit_WithoutDeletingArtifacts()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var context = CreateContext();
            var repository = CreateRepository(context);
            var orphan = await CreateResultAsync(seed, cancellationToken);

            for (var number = 1; number <= 3; number++)
            {
                var attempt = Assert.IsType<ImportJobWorkItem>(
                    await repository.ClaimNextPendingAsync(cancellationToken)
                );
                Assert.Equal(number, attempt.AttemptNumber);
                await ExpireAsync(seed, cancellationToken);

                Assert.True(await repository.RecoverNextExpiredAsync(cancellationToken));

                var saved = await ReadJobAsync(seed, cancellationToken);
                Assert.Equal(number, saved.AttemptNumber);
                Assert.Equal(number == 3 ? ImportJobStatus.Failed : ImportJobStatus.Pending, saved.Status);
            }

            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));
            Assert.False(await repository.RecoverNextExpiredAsync(cancellationToken));
            var failed = await ReadJobAsync(seed, cancellationToken);
            Assert.NotNull(failed.CompletedAtUtc);
            Assert.NotNull(failed.ErrorMessage);
            Assert.Null(failed.LeaseExpiresAtUtc);
            Assert.Equal(DatasetVersionStatus.Failed, (await ReadVersionAsync(seed, cancellationToken)).Status);
            Assert.True(await Storage.ExistsAsync(orphan.StorageKey, cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ConcurrentRecovery_ShouldRecoverTheSameExpiredJobExactlyOnce()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var claiming = CreateContext();
            Assert.NotNull(await CreateRepository(claiming).ClaimNextPendingAsync(cancellationToken));
            await ExpireAsync(seed, cancellationToken);
            var gate = new RecoveryGate();
            await using var winner = CreateContext(gate);
            await using var loser = CreateContext();
            var first = CreateRepository(winner).RecoverNextExpiredAsync(cancellationToken);

            try
            {
                await gate.Reached.Task.WaitAsync(cancellationToken);

                Assert.False(await CreateRepository(loser).RecoverNextExpiredAsync(cancellationToken));

                var unchanged = await ReadJobAsync(seed, cancellationToken);
                Assert.Equal(ImportJobStatus.Running, unchanged.Status);
                Assert.Equal(1L, unchanged.AttemptNumber);
            }
            finally
            {
                gate.Release.TrySetResult();
                Assert.True(await first.WaitAsync(cancellationToken));
            }

            Assert.False(await CreateRepository(loser).RecoverNextExpiredAsync(cancellationToken));
            Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed, cancellationToken)).Status);
            var second = Assert.IsType<ImportJobWorkItem>(
                await CreateRepository(loser).ClaimNextPendingAsync(cancellationToken)
            );
            Assert.Equal(2L, second.AttemptNumber);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData("00:00:02", "00:00:01", 3)]
    [InlineData("00:02:00", "00:00:00", 3)]
    [InlineData("00:02:00", "00:00:00.0000001", 3)]
    [InlineData("00:02:00", "00:01:00", 3)]
    [InlineData("00:02:00", "00:00:20", 0)]
    public void ImportOptions_ShouldRejectUnsafeConfiguration(
        string lease,
        string heartbeat,
        int attempts)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["ImportJobs:WorkerEnabled"] = "false",
                ["StorageCleanup:WorkerEnabled"] = "false",
                ["ImportJobs:LeaseDuration"] = lease,
                ["ImportJobs:HeartbeatInterval"] = heartbeat,
                ["ImportJobs:MaxAttempts"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        ).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ImportJobOptions>>().Value
        );
    }

    [Fact]
    public async Task Heartbeat_ShouldRecoverAfterOneDatabaseFailure_WhileConversionRemainsBlocked()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            var clock = new PulseTimeProvider();
            var fault = new HeartbeatFault();
            var storage = new ConversionGate(Storage);
            await using var provider = CreateProcessingProvider(storage, clock, fault);
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var processing = scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken);
            try
            {
                await storage.Reached.Task.WaitAsync(cancellationToken);
                Assert.Null(context.Database.CurrentTransaction);
                await using (var shortening = CreateContext())
                {
                    await shortening.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() + interval '30 seconds'
                        WHERE "Id" = {seed.Job.Id}
                        """, cancellationToken);
                }
                var before = await ReadJobAsync(seed, cancellationToken);

                clock.Pulse();
                await fault.FirstFailure.Task.WaitAsync(cancellationToken);
                await WaitForHeartbeatTransactionAsync(seed, cancellationToken);

                AssertJobEqual(before, await ReadJobAsync(seed, cancellationToken));
                Assert.False(processing.IsCompleted);
                Assert.False(storage.TokenWasCancelled);

                clock.Pulse();
                await fault.SecondSuccess.Task.WaitAsync(cancellationToken);
                await WaitForHeartbeatTransactionAsync(seed, cancellationToken);

                var renewed = await ReadJobAsync(seed, cancellationToken);
                Assert.True(renewed.LeaseExpiresAtUtc > before.LeaseExpiresAtUtc);
                Assert.Equal(ImportJobStatus.Running, renewed.Status);
                Assert.Equal(2, fault.ContextIds.Distinct().Count());
                Assert.DoesNotContain(context.ContextId.InstanceId, fault.ContextIds);
                Assert.False(storage.TokenWasCancelled);
            }
            finally
            {
                storage.Release.TrySetResult();
                Assert.True(await processing.WaitAsync(cancellationToken));
            }

            Assert.Equal(ImportJobStatus.Completed, (await ReadJobAsync(seed, cancellationToken)).Status);
            Assert.True(await Storage.ExistsAsync(storage.SavedKey!, cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task ConfirmedOwnershipLoss_ShouldCancelConversion_WithoutDeletingEitherAttempt()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            var clock = new PulseTimeProvider();
            var storage = new ConversionGate(Storage);
            await using var provider = CreateProcessingProvider(storage, clock);
            await using var scope = provider.CreateAsyncScope();
            var processing = scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken);
            ImportJobWorkItem next;
            try
            {
                await storage.Reached.Task.WaitAsync(cancellationToken);
                await ExpireAsync(seed, cancellationToken);
                await using var context = CreateContext();
                var repository = CreateRepository(context);
                Assert.True(await repository.RecoverNextExpiredAsync(cancellationToken));
                next = Assert.IsType<ImportJobWorkItem>(
                    await repository.ClaimNextPendingAsync(cancellationToken)
                );

                clock.Pulse();
                Assert.True(await processing.WaitAsync(cancellationToken));

                Assert.True(storage.TokenWasCancelled);
                Assert.Equal(2L, next.AttemptNumber);
                Assert.Equal(ImportJobStatus.Running, (await ReadJobAsync(seed, cancellationToken)).Status);
                Assert.True(await Storage.ExistsAsync(storage.SavedKey!, cancellationToken));
                var result = await CreateResultAsync(seed, cancellationToken);
                Assert.Equal(
                    ImportJobCompletionOutcome.Completed,
                    await repository.CompleteAsync(
                        next,
                        result.StorageKey,
                        result.RowCount,
                        result.ColumnCount,
                        cancellationToken
                    )
                );
                Assert.Equal(result.StorageKey, (await ReadVersionAsync(seed, cancellationToken)).StorageKey);
                Assert.True(await Storage.ExistsAsync(result.StorageKey, cancellationToken));
                Assert.Equal(0, storage.DeleteCalls);
            }
            finally
            {
                storage.Release.TrySetResult();
                await processing.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCancellation_ShouldPreserveArtifacts_BeforeOrAfterPublication(
        bool afterCommit)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        using var hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seed = await SeedAsync(cancellationToken);
        try
        {
            var storage = new ConversionGate(Storage);
            var commit = new CommitAcknowledgementFault(hostCancellation, throwAfterCommit: false);
            await using var provider = CreateProcessingProvider(storage, new PulseTimeProvider(), commit);
            await using var scope = provider.CreateAsyncScope();
            var processing = scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(hostCancellation.Token);
            await storage.Reached.Task.WaitAsync(cancellationToken);
            if (afterCommit)
            {
                storage.Release.TrySetResult();
                Assert.True(await processing.WaitAsync(cancellationToken));
            }
            else
            {
                await hostCancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing.WaitAsync(cancellationToken));
            }

            var saved = await ReadJobAsync(seed, cancellationToken);
            Assert.Equal(afterCommit ? ImportJobStatus.Completed : ImportJobStatus.Running, saved.Status);
            Assert.True(await Storage.ExistsAsync(storage.SavedKey!, cancellationToken));
            Assert.Equal(0, storage.DeleteCalls);
            if (afterCommit)
            {
                Assert.Equal(DatasetVersionStatus.Ready, (await ReadVersionAsync(seed, cancellationToken)).Status);
                Assert.Null(saved.LeaseExpiresAtUtc);
            }
            else
            {
                await ExpireAsync(seed, cancellationToken);
                await using var recovery = CreateContext();
                Assert.True(await CreateRepository(recovery).RecoverNextExpiredAsync(cancellationToken));
                Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed, cancellationToken)).Status);
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitReconciliation_ShouldUseAFreshContext_AndNeverRepublish(
        bool verificationInitiallyUnavailable)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            var commit = new CommitAcknowledgementFault();
            var reads = new VerificationFault(commit, verificationInitiallyUnavailable);
            await using var provider = CreateProcessingProvider(
                Storage,
                new PulseTimeProvider(),
                commit,
                reads
            );
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var processing = scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken);

            if (verificationInitiallyUnavailable)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => processing);
                Assert.IsType<Npgsql.NpgsqlException>(error.InnerException);
            }
            else
            {
                Assert.True(await processing);
            }

            Assert.Equal(2, commit.CommitCount);
            Assert.Equal(context.ContextId.InstanceId, commit.PoisonedContextId);
            Assert.DoesNotContain(context.ContextId.InstanceId, reads.VerificationContextIds);
            var completed = await ReadJobAsync(seed, cancellationToken);
            var version = await ReadVersionAsync(seed, cancellationToken);
            Assert.Equal(ImportJobStatus.Completed, completed.Status);
            Assert.Equal(DatasetVersionStatus.Ready, version.Status);
            Assert.NotNull(version.StorageKey);
            Assert.True(await Storage.ExistsAsync(version.StorageKey, cancellationToken));
            var receipt = new ImportJobWorkItem(
                context.ImportJobs.Local.Single(),
                seed.Version,
                seed.Dataset,
                1
            );

            await using var verificationScope = provider.CreateAsyncScope();
            var repository = verificationScope.ServiceProvider.GetRequiredService<IImportJobRepository>();

            Assert.True(await repository.IsCompletedAsync(
                receipt,
                version.StorageKey,
                version.RowCount!.Value,
                version.ColumnCount!.Value,
                cancellationToken
            ));
            Assert.False(await repository.RecoverNextExpiredAsync(cancellationToken));
            Assert.Null(await repository.ClaimNextPendingAsync(cancellationToken));
            AssertJobEqual(completed, await ReadJobAsync(seed, cancellationToken));
            Assert.Equal(2, commit.CommitCount);
            Assert.True(await Storage.ExistsAsync(version.StorageKey, cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task RecoveryService_ShouldLeaveActiveWorkAtStartup_AndRecoverExpiryOnALaterTick()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var context = CreateContext();
            Assert.NotNull(await CreateRepository(context).ClaimNextPendingAsync(cancellationToken));
            var clock = new PulseTimeProvider();
            var observer = new RecoveryObserver();
            await using var provider = CreateProcessingProvider(Storage, clock, observer);
            using var service = new ImportJobRecoveryService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IOptions<ImportJobOptions>>(),
                clock,
                NullLogger<ImportJobRecoveryService>.Instance
            );

            await service.StartAsync(cancellationToken);
            try
            {
                await observer.FirstPass.Task.WaitAsync(cancellationToken);
                Assert.Equal(ImportJobStatus.Running, (await ReadJobAsync(seed, cancellationToken)).Status);
                await ExpireAsync(seed, cancellationToken);

                clock.Pulse();
                await observer.SecondPass.Task.WaitAsync(cancellationToken);
                await WaitForHeartbeatTransactionAsync(seed, cancellationToken);

                Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed, cancellationToken)).Status);
                Assert.Equal(DatasetVersionStatus.Pending, (await ReadVersionAsync(seed, cancellationToken)).Status);
            }
            finally
            {
                await service.StopAsync(cancellationToken);
            }
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportFailure_ShouldRetryOnlyUnexpectedErrors(
        bool permanentMissingFile)
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            Exception failure = permanentMissingFile
                ? new FileNotFoundException("Missing source.")
                : new InvalidOperationException("Unexpected conversion failure.");
            var storage = new ReadFailureStorage(Storage, failure);
            await using var provider = CreateProcessingProvider(storage, new PulseTimeProvider());
            await using var scope = provider.CreateAsyncScope();

            Assert.True(await scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken));

            var saved = await ReadJobAsync(seed, cancellationToken);
            Assert.Equal(permanentMissingFile ? ImportJobStatus.Failed : ImportJobStatus.Running, saved.Status);
            if (!permanentMissingFile)
            {
                await ExpireAsync(seed, cancellationToken);
            }
            await using var context = CreateContext();

            Assert.Equal(!permanentMissingFile, await CreateRepository(context).RecoverNextExpiredAsync(cancellationToken));

            Assert.Equal(permanentMissingFile ? ImportJobStatus.Failed : ImportJobStatus.Pending,
                (await ReadJobAsync(seed, cancellationToken)).Status);
            Assert.Equal(permanentMissingFile ? DatasetVersionStatus.Failed : DatasetVersionStatus.Pending,
                (await ReadVersionAsync(seed, cancellationToken)).Status);
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task InvalidCsv_ShouldFailPermanently_WithoutARecoveryLoop()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await Storage.DeleteAsync(seed.Job.SourceStorageKey, cancellationToken);
            await using var bytes = new MemoryStream(new byte[] { 0xff, 0xfe, 0, 0, 0xff });
            await Storage.SaveAsync(seed.Job.SourceStorageKey, bytes, cancellationToken);
            await using var provider = CreateProcessingProvider(Storage, new PulseTimeProvider());
            await using var scope = provider.CreateAsyncScope();

            Assert.True(await scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken));

            var saved = await ReadJobAsync(seed, cancellationToken);
            Assert.Equal(ImportJobStatus.Failed, saved.Status);
            Assert.Equal(1L, saved.AttemptNumber);
            Assert.Null(saved.LeaseExpiresAtUtc);
            Assert.Equal(DatasetVersionStatus.Failed, (await ReadVersionAsync(seed, cancellationToken)).Status);
            await using var context = CreateContext();
            Assert.False(await CreateRepository(context).RecoverNextExpiredAsync(cancellationToken));
            Assert.Null(await CreateRepository(context).ClaimNextPendingAsync(cancellationToken));
        }
        finally
        {
            await CleanupAsync(seed);
        }
    }

    [Fact]
    public async Task NativeImporterBug_ShouldRemainRetryable_InsteadOfBeingClassifiedAsInvalidCsv()
    {
        using var deadline = CreateDeadline();
        var cancellationToken = deadline.Token;
        var seed = await SeedAsync(cancellationToken);
        try
        {
            await using var connection = new DuckDBConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM missing_importer_table";
            var failure = await Assert.ThrowsAsync<DuckDBException>(() =>
                command.ExecuteNonQueryAsync(cancellationToken)
            );
            Assert.Contains("missing_importer_table", failure.Message, StringComparison.Ordinal);
            Assert.NotEqual(DuckDBErrorType.InvalidInput, failure.ErrorType);
            Assert.NotEqual(DuckDBErrorType.Conversion, failure.ErrorType);
            var storage = new ReadFailureStorage(
                Storage,
                failure
            );
            await using var provider = CreateProcessingProvider(storage, new PulseTimeProvider());
            await using var scope = provider.CreateAsyncScope();

            Assert.True(await scope.ServiceProvider.GetRequiredService<ImportJobProcessor>()
                .ProcessNextAsync(cancellationToken));

            Assert.Equal(ImportJobStatus.Running, (await ReadJobAsync(seed, cancellationToken)).Status);
            await ExpireAsync(seed, cancellationToken);
            await using var context = CreateContext();
            Assert.True(await CreateRepository(context).RecoverNextExpiredAsync(cancellationToken));
            Assert.Equal(ImportJobStatus.Pending, (await ReadJobAsync(seed, cancellationToken)).Status);
            Assert.Equal(DatasetVersionStatus.Pending, (await ReadVersionAsync(seed, cancellationToken)).Status);
        }
        finally
        {
            await CleanupAsync(seed);
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

    private AppDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }
        return new AppDbContext(options.Options);
    }

    private static ImportJobRepository CreateRepository(AppDbContext context)
    {
        return new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            Options.Create(new ImportJobOptions())
        );
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        var project = new Project(
            Guid.NewGuid(),
            "Lease project"
        );
        var dataset = new Dataset(
            project.Id,
            "Lease dataset"
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
        await using var context = CreateContext();
        context.AddRange(project, dataset, version, job);
        await context.SaveChangesAsync(cancellationToken);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());
        await Storage.SaveAsync(job.SourceStorageKey, source, cancellationToken);
        return new Seed(project, dataset, version, job);
    }

    private async Task ExpireAsync(Seed seed, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE import_jobs SET "LeaseExpiresAtUtc" = clock_timestamp() - interval '1 second'
            WHERE "Id" = {seed.Job.Id}
            """, cancellationToken);
    }

    private async Task<ImportJob> ReadJobAsync(Seed seed, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        return await context.ImportJobs.AsNoTracking().SingleAsync(job => job.Id == seed.Job.Id, cancellationToken);
    }

    private async Task<DatasetVersion> ReadVersionAsync(Seed seed, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        return await context.DatasetVersions.AsNoTracking().SingleAsync(version => version.Id == seed.Version.Id, cancellationToken);
    }

    private static async Task<DateTime> ServerNowAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var values = await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .ToListAsync(cancellationToken);
        return Assert.Single(values);
    }

    private async Task<DuckDbCsvImportResult> CreateResultAsync(Seed seed, CancellationToken cancellationToken)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>().ImportAsync(
            seed.Job.SourceStorageKey,
            $"datasets/{seed.Dataset.Id:N}/versions/{seed.Version.Id:N}/attempts",
            cancellationToken
        );
    }

    private async Task CleanupAsync(Seed seed)
    {
        await using var context = CreateContext();
        await context.Projects.Where(project => project.Id == seed.Project.Id).ExecuteDeleteAsync(CancellationToken.None);
        await Storage.DeleteDirectoryAsync($"datasets/{seed.Dataset.Id:N}", CancellationToken.None);
    }

    private static void AssertJobEqual(ImportJob expected, ImportJob actual)
    {
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AttemptNumber, actual.AttemptNumber);
        Assert.Equal(expected.LeaseExpiresAtUtc, actual.LeaseExpiresAtUtc);
        Assert.Equal(expected.StartedAtUtc, actual.StartedAtUtc);
        Assert.Equal(expected.CompletedAtUtc, actual.CompletedAtUtc);
        Assert.Equal(expected.ErrorMessage, actual.ErrorMessage);
    }

    private sealed record Seed(
        Project Project,
        Dataset Dataset,
        DatasetVersion Version,
        ImportJob Job
    );

    private ServiceProvider CreateProcessingProvider(
        IFileStorage storage,
        PulseTimeProvider timeProvider,
        params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(ConnectionString).AddInterceptors(interceptors));
        services.AddSingleton(Options.Create(new ImportJobOptions()));
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(storage);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IProjectWriteTransactionFactory, ProjectWriteTransactionFactory>();
        services.AddScoped<IImportJobRepository, ImportJobRepository>();
        services.AddScoped<DuckDbCsvImporter>();
        services.AddScoped<ImportJobProcessor>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private async Task WaitForHeartbeatTransactionAsync(Seed seed, CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await using var transaction = await new ProjectWriteTransactionFactory(context).BeginAsync(
            seed.Project.Id,
            cancellationToken
        );
        Assert.NotNull(transaction);
    }

    private sealed class PulseTimeProvider : TimeProvider
    {
        private readonly List<PulseTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new PulseTimer(callback, state, dueTime);
            lock (_timers)
            {
                _timers.Add(timer);
            }
            return timer;
        }

        public void Pulse()
        {
            PulseTimer[] timers;
            lock (_timers)
            {
                timers = _timers.ToArray();
            }
            foreach (var timer in timers)
            {
                timer.Fire();
            }
        }

        private sealed class PulseTimer : ITimer
        {
            private readonly object _syncRoot = new();
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _enabled;

            public PulseTimer(TimerCallback callback, object? state, TimeSpan dueTime)
            {
                _callback = callback;
                _state = state;
                _enabled = dueTime != Timeout.InfiniteTimeSpan;
            }

            public void Fire()
            {
                lock (_syncRoot)
                {
                    if (_enabled)
                    {
                        _callback(_state);
                    }
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_syncRoot)
                {
                    _enabled = dueTime != Timeout.InfiniteTimeSpan;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_syncRoot)
                {
                    _enabled = false;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class HeartbeatFault : DbCommandInterceptor
    {
        public TaskCompletionSource FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondSuccess { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Guid> ContextIds { get; } = [];

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SET \"LeaseExpiresAtUtc\"", StringComparison.Ordinal))
            {
                ContextIds.Add(eventData.Context!.ContextId.InstanceId);
                if (ContextIds.Count == 1)
                {
                    FirstFailure.TrySetResult();
                    throw new Npgsql.NpgsqlException("Transient heartbeat database failure.", new TimeoutException());
                }
                SecondSuccess.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitAcknowledgementFault : DbTransactionInterceptor
    {
        private readonly CancellationTokenSource? _hostCancellation;
        private readonly bool _throwAfterCommit;
        public int CommitCount { get; private set; }
        public Guid? PoisonedContextId { get; private set; }

        public CommitAcknowledgementFault(
            CancellationTokenSource? hostCancellation = null,
            bool throwAfterCommit = true)
        {
            _hostCancellation = hostCancellation;
            _throwAfterCommit = throwAfterCommit;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            CommitCount++;
            if (CommitCount == 2)
            {
                PoisonedContextId = eventData.Context!.ContextId.InstanceId;
                _hostCancellation?.Cancel();
                if (_throwAfterCommit)
                {
                    throw new IOException("Acknowledgement lost after the actual PostgreSQL commit.");
                }
            }
            return Task.CompletedTask;
        }
    }

    private sealed class VerificationFault : DbCommandInterceptor
    {
        private readonly CommitAcknowledgementFault _commit;
        private bool _failFreshRead;
        public List<Guid> VerificationContextIds { get; } = [];

        public VerificationFault(CommitAcknowledgementFault commit, bool failFreshRead)
        {
            _commit = commit;
            _failFreshRead = failFreshRead;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_commit.PoisonedContextId is { } poisoned)
            {
                var current = eventData.Context!.ContextId.InstanceId;
                if (poisoned == current)
                {
                    throw new InvalidOperationException("The original context cannot query after its connection failed.");
                }
                if (command.CommandText.Contains("EXISTS", StringComparison.Ordinal)
                    && command.CommandText.Contains("\"RowCount\"", StringComparison.Ordinal))
                {
                    VerificationContextIds.Add(current);
                    if (_failFreshRead)
                    {
                        _failFreshRead = false;
                        throw new Npgsql.NpgsqlException("Database is temporarily unavailable for verification.", new TimeoutException());
                    }
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecoveryObserver : DbCommandInterceptor
    {
        private int _count;
        public TaskCompletionSource FirstPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM import_jobs AS expired_job", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _count) == 1)
                {
                    FirstPass.TrySetResult();
                }
                else
                {
                    SecondPass.TrySetResult();
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ConversionGate : IFileStorage
    {
        private readonly IFileStorage _inner;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SavedKey { get; private set; }
        public bool TokenWasCancelled { get; private set; }
        public int DeleteCalls { get; private set; }

        public ConversionGate(IFileStorage inner)
        {
            _inner = inner;
        }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(storageKey, content, cancellationToken);
            SavedKey = storageKey;
            Reached.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TokenWasCancelled = true;
                throw;
            }
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(storageKey, cancellationToken);

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return _inner.DeleteAsync(storageKey, cancellationToken);
        }

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
        }
    }

    private sealed class ReadFailureStorage : IFileStorage
    {
        private readonly IFileStorage _inner;
        private readonly Exception _failure;

        public ReadFailureStorage(IFileStorage inner, Exception failure)
        {
            _inner = inner;
            _failure = failure;
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromException<Stream>(_failure);

        public Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            _inner.SaveAsync(storageKey, content, cancellationToken);

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(storageKey, cancellationToken);

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
    }

    private sealed class RecoveryGate : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM import_jobs AS expired_job", StringComparison.Ordinal))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
