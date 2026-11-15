using System.Data.Common;

using AnalyticDashboard.Application.Datasets.ImportCsvDataset;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;
using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class PendingDatasetImportCommitTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public PendingDatasetImportCommitTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_ShouldConfirmActualCommitInAFreshContext_AndKeepSourceUsable(
        bool cancelAfterCommit)
    {
        using var deadline = CreateDeadline();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var project = await SeedProjectAsync(deadline.Token);
        var fault = new CreationFault(
            beforeCommit: false,
            cancelAfterCommit: cancelAfterCommit ? requestCancellation : null
        );
        var saves = new SaveObservation();
        var verification = new VerificationObservation();
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(fault, saves);
        var storage = new ObservedStorage();
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var result = await handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                requestCancellation.Token
            );

            var accepted = Assert.IsType<ImportCsvDatasetResult.Accepted>(result);
            Assert.Equal(1, fault.CommitAttempts);
            Assert.Equal(1, fault.Committed);
            Assert.Equal(1, saves.SavedCalls);
            Assert.Equal(0, storage.DeleteCalls);
            Assert.Equal(1, storage.SaveCalls);
            Assert.NotEqual(
                context.ContextId.InstanceId,
                Assert.Single(verification.ContextIds)
            );
            Assert.Equal(cancelAfterCommit, requestCancellation.IsCancellationRequested);
            var job = await AssertMetadataAsync(project, storage, expectedCount: 1, deadline.Token);
            Assert.NotNull(job);
            Assert.Equal(accepted.ImportJobId, job.Id);
            Assert.Equal(accepted.DatasetVersionId, job.DatasetVersionId);
            Assert.Equal(SourceDatasetId(storage), accepted.DatasetId);
            Assert.Equal(ImportJobStatus.Pending, job.Status);
            await AssertSourceAsync(storage, deadline.Token);

            await ProcessAndAssertReadyAsync(accepted, deadline.Token);
            Assert.True(await storage.ExistsAsync(storage.SavedKey!, deadline.Token));
            await AssertMetadataAsync(project, storage, expectedCount: 1, deadline.Token);
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Fact]
    public async Task Creation_ShouldRetainCommittedSource_WhenFreshVerificationIsUnavailable()
    {
        using var deadline = CreateDeadline();
        var project = await SeedProjectAsync(deadline.Token);
        var fault = new CreationFault(beforeCommit: false);
        var saves = new SaveObservation();
        var verification = new VerificationObservation { Unavailable = true };
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(fault, saves);
        var storage = new ObservedStorage();
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                deadline.Token
            ));

            Assert.Same(fault.Failure, exception);
            Assert.Equal(1, fault.Committed);
            Assert.Equal(1, fault.CommitAttempts);
            Assert.Equal(1, saves.SavedCalls);
            Assert.Single(verification.ContextIds);
            Assert.Equal(0, storage.DeleteCalls);
            var job = await AssertMetadataAsync(project, storage, expectedCount: 1, deadline.Token);
            Assert.NotNull(job);
            await AssertSourceAsync(storage, deadline.Token);

            await ProcessAndAssertReadyAsync(
                new ImportCsvDatasetResult.Accepted(
                    SourceDatasetId(storage),
                    job.DatasetVersionId,
                    job.Id
                ),
                deadline.Token
            );
            await AssertMetadataAsync(project, storage, expectedCount: 1, deadline.Token);
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Fact]
    public async Task Creation_ShouldRetainSource_WhenNegativeVerificationPrecedesActualCommit()
    {
        using var deadline = CreateDeadline();
        var project = await SeedProjectAsync(deadline.Token);
        var fault = new CreationFault(beforeCommit: true);
        var saves = new SaveObservation();
        var lateCommits = 0;
        var verification = new VerificationObservation
        {
            AfterRead = async token =>
            {
                Assert.NotNull(fault.PendingTransaction);
                await fault.PendingTransaction.CommitAsync(token);
                lateCommits++;
            }
        };
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(fault, saves);
        var storage = new ObservedStorage();
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                deadline.Token
            ));

            Assert.Same(fault.Failure, exception);
            Assert.Equal(1, fault.CommitAttempts);
            Assert.Equal(1, lateCommits);
            Assert.Equal(1, saves.SavedCalls);
            Assert.Single(verification.ContextIds);
            Assert.Equal(0, storage.DeleteCalls);
            var job = await AssertMetadataAsync(project, storage, expectedCount: 1, deadline.Token);
            Assert.NotNull(job);
            await AssertSourceAsync(storage, deadline.Token);
            await ProcessAndAssertReadyAsync(
                new ImportCsvDatasetResult.Accepted(
                    SourceDatasetId(storage),
                    job.DatasetVersionId,
                    job.Id
                ),
                deadline.Token
            );
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_ShouldRollbackAllInsertedMetadata_AndRemoveSource(
        bool cancelBeforeCommit)
    {
        using var deadline = CreateDeadline();
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var project = await SeedProjectAsync(deadline.Token);
        var transactions = new CreationFault();
        var saves = new SaveObservation(
            failAfterSave: true,
            cancelBeforeCommit: cancelBeforeCommit ? requestCancellation : null
        );
        var verification = new VerificationObservation();
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(transactions, saves);
        var storage = new ObservedStorage();
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                requestCancellation.Token
            ));

            Assert.Same(saves.Failure, exception);
            Assert.Equal(1, saves.SavedCalls);
            Assert.Equal(3, saves.SavedEntries);
            Assert.Equal(0, transactions.CommitAttempts);
            Assert.Equal(1, transactions.RolledBack);
            Assert.Empty(verification.ContextIds);
            Assert.Equal(1, storage.DeleteCalls);
            Assert.False(await storage.ExistsAsync(storage.SavedKey!, deadline.Token));
            await AssertMetadataAsync(project, storage, expectedCount: 0, deadline.Token);
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Fact]
    public async Task Creation_ShouldRollbackOnActualForeignKeyViolation_AfterProjectDeletion()
    {
        using var deadline = CreateDeadline();
        var project = await SeedProjectAsync(deadline.Token);
        var transactions = new CreationFault();
        var verification = new VerificationObservation();
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(transactions);
        var storage = new ObservedStorage
        {
            AfterSave = async token =>
            {
                await using var deletingContext = CreateContext();
                Assert.Equal(1, await deletingContext.Projects
                    .Where(item => item.Id == project.Id)
                    .ExecuteDeleteAsync(token));
            }
        };
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                deadline.Token
            ));

            Assert.Equal(
                PostgresErrorCodes.ForeignKeyViolation,
                Assert.IsType<PostgresException>(exception.InnerException).SqlState
            );
            Assert.Equal(0, transactions.CommitAttempts);
            Assert.Equal(1, transactions.RolledBack);
            Assert.Empty(verification.ContextIds);
            Assert.Equal(1, storage.DeleteCalls);
            Assert.False(await storage.ExistsAsync(storage.SavedKey!, deadline.Token));
            await AssertMetadataAsync(project, storage, expectedCount: 0, deadline.Token);
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Fact]
    public async Task Creation_ShouldRetainSource_WhenRollbackAcknowledgementIsLost()
    {
        using var deadline = CreateDeadline();
        var project = await SeedProjectAsync(deadline.Token);
        var transactions = new CreationFault { LoseRollbackAcknowledgement = true };
        var saves = new SaveObservation(failAfterSave: true);
        var verification = new VerificationObservation();
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(transactions, saves);
        var storage = new ObservedStorage();
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("Country,Amount\nPL,10\nDE,20\n"u8.ToArray());

        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                deadline.Token
            ));

            Assert.Same(saves.Failure, exception);
            Assert.Equal(1, transactions.RolledBack);
            Assert.Single(verification.ContextIds);
            Assert.Equal(0, storage.DeleteCalls);
            await AssertMetadataAsync(project, storage, expectedCount: 0, deadline.Token);
            await AssertSourceAsync(storage, deadline.Token);
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    [Fact]
    public async Task Creation_ShouldNotDeleteAnExistingArtifact_WhenSourceCreationFails()
    {
        using var deadline = CreateDeadline();
        var project = await SeedProjectAsync(deadline.Token);
        var saves = new SaveObservation();
        var verification = new VerificationObservation();
        await using var verificationServices = CreateVerificationServices(verification);
        await using var context = CreateContext(saves);
        var storage = new ObservedStorage { SimulateCollision = true };
        var handler = CreateHandler(context, verificationServices, storage);
        await using var source = new MemoryStream("replacement"u8.ToArray());

        try
        {
            await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
                new ImportCsvDatasetCommand(
                    project.Id,
                    project.OwnerId,
                    source,
                    "source.csv"
                ),
                deadline.Token
            ));

            Assert.Equal(0, saves.SavedCalls);
            Assert.Empty(verification.ContextIds);
            Assert.Equal(0, storage.DeleteCalls);
            await AssertMetadataAsync(project, storage, expectedCount: 0, deadline.Token);
            await using var existing = await storage.OpenReadAsync(storage.SavedKey!, deadline.Token);
            using var reader = new StreamReader(existing);
            Assert.Equal("existing artifact", await reader.ReadToEndAsync(deadline.Token));
        }
        finally
        {
            await CleanupAsync(project, storage);
        }
    }

    private ImportCsvDatasetHandler CreateHandler(
        AppDbContext context,
        IServiceProvider verificationServices,
        IFileStorage storage)
    {
        return new ImportCsvDatasetHandler(
            new ProjectRepository(context),
            new PendingDatasetImportRepository(
                context,
                verificationServices.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<PendingDatasetImportRepository>.Instance
            ),
            storage
        );
    }

    private string GetConnectionString()
    {
        using var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Database.GetConnectionString()!;
    }

    private AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GetConnectionString())
            .AddInterceptors(interceptors)
            .Options);
    }

    private ServiceProvider CreateVerificationServices(IInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(GetConnectionString())
            .AddInterceptors(interceptor));
        return services.BuildServiceProvider();
    }

    private async Task<Project> SeedProjectAsync(CancellationToken cancellationToken)
    {
        var project = new Project(
            Guid.NewGuid(),
            "Pending import commit"
        );
        await using var context = CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken);
        return project;
    }

    private async Task<ImportJob?> AssertMetadataAsync(
        Project project,
        ObservedStorage storage,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        var datasetId = SourceDatasetId(storage);
        Assert.Equal(expectedCount, await context.Datasets.CountAsync(
            item => item.Id == datasetId && item.ProjectId == project.Id,
            cancellationToken
        ));
        Assert.Equal(expectedCount, await context.DatasetVersions.CountAsync(
            item => item.DatasetId == datasetId,
            cancellationToken
        ));
        var jobs = await context.ImportJobs.AsNoTracking()
            .Where(item => item.SourceStorageKey == storage.SavedKey)
            .ToListAsync(cancellationToken);
        Assert.Equal(expectedCount, jobs.Count);
        return jobs.SingleOrDefault();
    }

    private static Guid SourceDatasetId(ObservedStorage storage)
    {
        Assert.NotNull(storage.SavedKey);
        return Guid.ParseExact(storage.SavedKey.Split('/')[1], "N");
    }

    private static async Task AssertSourceAsync(ObservedStorage storage, CancellationToken cancellationToken)
    {
        Assert.True(await storage.ExistsAsync(storage.SavedKey!, cancellationToken));
        await using var source = await storage.OpenReadAsync(storage.SavedKey!, cancellationToken);
        using var reader = new StreamReader(source);
        Assert.Equal(
            "Country,Amount\nPL,10\nDE,20\n",
            await reader.ReadToEndAsync(cancellationToken)
        );
    }

    private async Task ProcessAndAssertReadyAsync(
        ImportCsvDatasetResult.Accepted accepted,
        CancellationToken cancellationToken)
    {
        await using var workerScope = _fixture.Services.CreateAsyncScope();
        var processor = workerScope.ServiceProvider.GetRequiredService<ImportJobProcessor>();

        Assert.True(await processor.ProcessNextAsync(cancellationToken));

        await using var context = CreateContext();
        var job = await context.ImportJobs.SingleAsync(
            item => item.Id == accepted.ImportJobId,
            cancellationToken
        );
        var version = await context.DatasetVersions.SingleAsync(
            item => item.Id == accepted.DatasetVersionId,
            cancellationToken
        );
        var dataset = await context.Datasets.SingleAsync(
            item => item.Id == accepted.DatasetId,
            cancellationToken
        );

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1L, job.AttemptNumber);
        Assert.Equal(DatasetVersionStatus.Ready, version.Status);
        Assert.Equal(2, version.RowCount);
        Assert.Equal(2, version.ColumnCount);
        Assert.Equal(version.Id, dataset.CurrentVersionId);
        Assert.NotNull(version.StorageKey);
        Assert.True(await workerScope.ServiceProvider.GetRequiredService<IFileStorage>()
            .ExistsAsync(version.StorageKey, cancellationToken));
    }

    private async Task CleanupAsync(Project project, ObservedStorage storage)
    {
        await using var context = CreateContext();
        await context.Projects.Where(item => item.Id == project.Id)
            .ExecuteDeleteAsync(CancellationToken.None);
        if (storage.SavedKey is not null)
        {
            await storage.DeleteDirectoryAsync(
                $"datasets/{SourceDatasetId(storage):N}",
                CancellationToken.None
            );
        }
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private sealed class CreationFault : DbTransactionInterceptor
    {
        private readonly bool? _beforeCommit;
        private readonly CancellationTokenSource? _cancelAfterCommit;

        public CreationFault(
            bool? beforeCommit = null,
            CancellationTokenSource? cancelAfterCommit = null)
        {
            _beforeCommit = beforeCommit;
            _cancelAfterCommit = cancelAfterCommit;
        }

        public Exception Failure { get; private set; } = new IOException("Lost commit acknowledgement.");
        public DbTransaction? PendingTransaction { get; private set; }
        public int CommitAttempts { get; private set; }
        public int Committed { get; private set; }
        public int RolledBack { get; private set; }
        public bool LoseRollbackAcknowledgement { get; init; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            if (_beforeCommit == true)
            {
                PendingTransaction = transaction;
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Committed++;
            if (_beforeCommit == false)
            {
                if (_cancelAfterCommit is not null)
                {
                    _cancelAfterCommit.Cancel();
                    Failure = new OperationCanceledException(_cancelAfterCommit.Token);
                }
                throw Failure;
            }
            return Task.CompletedTask;
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            RolledBack++;
            if (LoseRollbackAcknowledgement)
            {
                throw new IOException("Lost rollback acknowledgement.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class SaveObservation : SaveChangesInterceptor
    {
        private readonly bool _failAfterSave;
        private readonly CancellationTokenSource? _cancelBeforeCommit;

        public SaveObservation(
            bool failAfterSave = false,
            CancellationTokenSource? cancelBeforeCommit = null)
        {
            _failAfterSave = failAfterSave;
            _cancelBeforeCommit = cancelBeforeCommit;
            Failure = cancelBeforeCommit is null
                ? new IOException("Failure after INSERTs, before COMMIT.")
                : new OperationCanceledException(cancelBeforeCommit.Token);
        }

        public Exception Failure { get; }
        public int SavedCalls { get; private set; }
        public int SavedEntries { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SavedCalls++;
            SavedEntries = result;
            if (_failAfterSave)
            {
                _cancelBeforeCommit?.Cancel();
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class VerificationObservation : DbCommandInterceptor
    {
        public bool Unavailable { get; init; }
        public Func<CancellationToken, Task>? AfterRead { get; init; }
        public List<Guid> ContextIds { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ContextIds.Add(eventData.Context!.ContextId.InstanceId);
            if (Unavailable)
            {
                throw new IOException("Verification connection unavailable.");
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (AfterRead is not null)
            {
                await AfterRead(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObservedStorage : IFileStorage
    {
        private readonly LocalFileStorage _inner = new();

        public string? SavedKey { get; private set; }
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool SimulateCollision { get; init; }
        public Func<CancellationToken, Task>? AfterSave { get; init; }

        public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
        {
            SaveCalls++;
            SavedKey = storageKey;
            if (SimulateCollision)
            {
                await using var existing = new MemoryStream("existing artifact"u8.ToArray());
                await _inner.SaveAsync(storageKey, existing, cancellationToken);
            }
            await _inner.SaveAsync(storageKey, content, cancellationToken);
            if (AfterSave is not null)
            {
                await AfterSave(cancellationToken);
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

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
    }
}
