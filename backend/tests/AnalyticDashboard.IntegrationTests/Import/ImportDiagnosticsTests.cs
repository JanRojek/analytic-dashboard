using System.Collections.Concurrent;

using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class ImportDiagnosticsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ImportDiagnosticsTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("missing")]
    [InlineData("unexpected")]
    [InlineData("host_cancelled")]
    [InlineData("fenced")]
    public async Task Processing_ShouldIdentifyAttemptOutcomeAndDuration_WithoutLoggingPrivateData(
        string outcome)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var token = cancellation.Token;
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var project = new Project(
            Guid.NewGuid(),
            "Import diagnostics"
        );
        var dataset = new Dataset(
            project.Id,
            "Private dataset"
        );
        var version = new DatasetVersion(
            dataset.Id,
            1,
            "private-person@example.com.csv"
        );
        var job = new ImportJob(
            version.Id,
            version.OriginalFileName,
            $"datasets/{dataset.Id:N}/private-person@example.com/source.csv"
        );
        context.AddRange(project, dataset, version, job);
        await context.SaveChangesAsync(token);

        var logs = new CapturingLogger<ImportJobProcessor>();
        var repositoryLogs = new CapturingLogger<ImportJobRepository>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ImportJobOptions>>();
        var repository = new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            options,
            repositoryLogs
        );
        var observedStorage = new BeforeReadStorage(storage, async readToken =>
        {
            if (outcome == "unexpected")
            {
                throw new IOException("PRIVATE_CSV_VALUE private-person@example.com");
            }
            if (outcome == "host_cancelled")
            {
                await cancellation.CancelAsync();
                readToken.ThrowIfCancellationRequested();
            }
            if (outcome == "fenced")
            {
                using var newerScope = _fixture.Services.CreateScope();
                var newerContext = newerScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await newerContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE import_jobs SET "LeaseExpiresAtUtc" = '-infinity'
                    WHERE "Id" = {job.Id}
                    """, readToken);
                var newerRepository = newerScope.ServiceProvider.GetRequiredService<IImportJobRepository>();
                Assert.True(await newerRepository.RecoverNextExpiredAsync(readToken));
                var newer = Assert.IsType<ImportJobWorkItem>(
                    await newerRepository.ClaimNextPendingAsync(readToken)
                );
                Assert.Equal(job.Id, newer.Job.Id);
                Assert.Equal(2L, newer.AttemptNumber);
            }
        });
        var processor = new ImportJobProcessor(
            repository,
            new DuckDbCsvImporter(
                observedStorage,
                NullLogger<DuckDbCsvImporter>.Instance,
                options
            ),
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            options,
            TimeProvider.System,
            logs
        );

        try
        {
            if (outcome != "missing")
            {
                await using var source = new MemoryStream("Category,Value\nPRIVATE_CSV_VALUE,10\n"u8.ToArray());
                await storage.SaveAsync(job.SourceStorageKey, source, token);
            }

            if (outcome == "host_cancelled")
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    processor.ProcessNextAsync(token)
                );
            }
            else
            {
                Assert.True(await processor.ProcessNextAsync(token));
            }

            var entries = logs.Entries.ToArray();
            var started = Assert.Single(entries, entry => entry.Template.EndsWith("started.", StringComparison.Ordinal));
            Assert.Equal(LogLevel.Information, started.Level);
            var finished = Assert.Single(entries, entry => entry.Fields.ContainsKey("ElapsedMilliseconds"));
            Assert.True(Assert.IsType<double>(finished.Fields["ElapsedMilliseconds"]) >= 0);
            foreach (var entry in entries)
            {
                Assert.Equal(job.Id, entry.Fields["ImportJobId"]);
                Assert.Equal(1L, entry.Fields["AttemptNumber"]);
                Assert.Null(entry.Exception);
                Assert.DoesNotContain("PRIVATE_CSV_VALUE", entry.Message);
                Assert.DoesNotContain("private-person@example.com", entry.Message);
                Assert.DoesNotContain(job.SourceStorageKey, entry.Message);
            }

            var expectedMessage = outcome switch
            {
                "completed" => "completed with",
                "missing" => "failed permanently",
                "unexpected" => "shortened its lease for recovery",
                "host_cancelled" => "cancelled because the host is stopping",
                _ => "no longer owns the job"
            };
            Assert.Single(entries, entry => entry.Message.Contains(expectedMessage, StringComparison.Ordinal));
            if (outcome == "unexpected")
            {
                Assert.Contains(entries, entry => Equals(entry.Fields.GetValueOrDefault("ErrorType"), "IOException"));
            }
            if (outcome != "completed")
            {
                Assert.DoesNotContain(entries, entry => entry.Message.Contains("completed with", StringComparison.Ordinal));
            }

            context.ChangeTracker.Clear();
            var savedJob = await context.ImportJobs.SingleAsync(item => item.Id == job.Id, CancellationToken.None);
            var savedVersion = await context.DatasetVersions.SingleAsync(item => item.Id == version.Id, CancellationToken.None);
            var savedDataset = await context.Datasets.SingleAsync(item => item.Id == dataset.Id, CancellationToken.None);
            Assert.Equal(outcome switch
            {
                "completed" => ImportJobStatus.Completed,
                "missing" => ImportJobStatus.Failed,
                _ => ImportJobStatus.Running
            }, savedJob.Status);
            Assert.Equal(outcome == "fenced" ? 2L : 1L, savedJob.AttemptNumber);
            Assert.Equal(outcome == "completed" ? DatasetVersionStatus.Ready
                : outcome == "missing" ? DatasetVersionStatus.Failed : DatasetVersionStatus.Pending, savedVersion.Status);
            Assert.Equal(outcome == "completed" ? version.Id : (Guid?)null, savedDataset.CurrentVersionId);
        }
        finally
        {
            await context.Projects.Where(item => item.Id == project.Id).ExecuteDeleteAsync(CancellationToken.None);
            await storage.DeleteDirectoryAsync($"datasets/{dataset.Id:N}", CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(1, "lease_expired", ImportJobStatus.Pending)]
    [InlineData(3, "attempt_limit_reached", ImportJobStatus.Failed)]
    public async Task Recovery_ShouldLogTheCommittedTransitionAndReason(
        long attempt,
        string reason,
        ImportJobStatus expectedStatus)
    {
        var token = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project(
            Guid.NewGuid(),
            "Recovery diagnostics"
        );
        var dataset = new Dataset(
            project.Id,
            "Recovery dataset"
        );
        var version = new DatasetVersion(
            dataset.Id,
            1,
            "private-person@example.com.csv"
        );
        var job = new ImportJob(
            version.Id,
            version.OriginalFileName,
            $"datasets/{dataset.Id:N}/source.csv"
        );
        context.AddRange(project, dataset, version, job);
        context.Entry(job).Property(item => item.Status).CurrentValue = ImportJobStatus.Running;
        context.Entry(job).Property(item => item.AttemptNumber).CurrentValue = attempt;
        context.Entry(job).Property(item => item.StartedAtUtc).CurrentValue = DateTime.UtcNow;
        context.Entry(job).Property(item => item.LeaseExpiresAtUtc).CurrentValue = DateTime.UnixEpoch;
        await context.SaveChangesAsync(token);
        var logs = new CapturingLogger<ImportJobRepository>();
        var repository = new ImportJobRepository(
            context,
            new ProjectWriteTransactionFactory(context),
            scope.ServiceProvider.GetRequiredService<IOptions<ImportJobOptions>>(),
            logs
        );

        try
        {
            Assert.True(await repository.RecoverNextExpiredAsync(token));
            Assert.False(await repository.RecoverNextExpiredAsync(token));

            var entry = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal(job.Id, entry.Fields["ImportJobId"]);
            Assert.Equal(attempt, entry.Fields["AttemptNumber"]);
            Assert.Equal(expectedStatus, entry.Fields["Status"]);
            Assert.Equal(reason, entry.Fields["RecoveryReason"]);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(version.OriginalFileName, entry.Message);
            Assert.Equal(expectedStatus, await context.ImportJobs.AsNoTracking()
                .Where(item => item.Id == job.Id).Select(item => item.Status).SingleAsync(token));
        }
        finally
        {
            await context.Projects.Where(item => item.Id == project.Id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    private sealed class BeforeReadStorage : IFileStorage
    {
        private readonly IFileStorage _inner;
        private readonly Func<CancellationToken, Task> _beforeRead;

        public BeforeReadStorage(IFileStorage inner, Func<CancellationToken, Task> beforeRead)
        {
            _inner = inner;
            _beforeRead = beforeRead;
        }

        public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            await _beforeRead(cancellationToken);
            return await _inner.OpenReadAsync(storageKey, cancellationToken);
        }

        public Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            _inner.SaveAsync(storageKey, content, cancellationToken);

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.ExistsAsync(storageKey, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(storageKey, cancellationToken);

        public Task DeleteDirectoryAsync(string storageKey, CancellationToken cancellationToken) =>
            _inner.DeleteDirectoryAsync(storageKey, cancellationToken);
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Template,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields
    );

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state)
                .ToDictionary(item => item.Key, item => item.Value);
            Entries.Enqueue(new LogEntry(
                logLevel,
                Assert.IsType<string>(fields["{OriginalFormat}"]),
                formatter(state, exception),
                exception,
                fields
            ));
        }
    }
}
