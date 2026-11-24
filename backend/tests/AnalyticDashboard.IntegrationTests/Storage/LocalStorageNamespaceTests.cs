using System.Diagnostics;

using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.IntegrationTests.Storage;

public sealed class LocalStorageNamespaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstallAndClose_ShouldSerializeAcrossIndependentProcesses(bool installFirst)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-race-");
        var datasetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var storage = new LocalFileStorage(directory.FullName);
        var key = $"datasets/{datasetId:N}/result.parquet";

        try
        {
            await using var winner = Child.Start(
                directory.FullName,
                datasetId,
                installFirst ? "install" : "close",
                "acquired"
            );
            await using var contender = Child.Start(
                directory.FullName,
                datasetId,
                installFirst ? "close" : "install"
            );
            Assert.NotEqual(winner.Id, contender.Id);
            Assert.NotEqual(Environment.ProcessId, winner.Id);
            Assert.NotEqual(Environment.ProcessId, contender.Id);
            await winner.ExpectAsync("prepared", token);
            await contender.ExpectAsync("prepared", token);

            await winner.ContinueAsync(token);
            await winner.ExpectAsync("locked", token);
            await contender.ContinueAsync(token);
            await contender.ExpectAsync("blocked", token);

            await using (var other = new MemoryStream("unrelated namespace"u8.ToArray()))
            {
                await storage.SaveAsync($"datasets/{otherId:N}/other.csv", other, token);
            }
            Assert.False(await storage.ExistsAsync(key, token));

            await winner.ContinueAsync(token);
            await winner.ExpectAsync(installFirst ? "installed" : "closed", token);
            await contender.ExpectAsync(installFirst ? "closed" : "rejected", token);
            await winner.ExpectSuccessAsync(token);
            await contender.ExpectSuccessAsync(token);

            Assert.Equal(LocalStorageNamespaceState.Closed, await storage.GetNamespaceStateAsync(datasetId, token));
            Assert.Equal(installFirst, await storage.ExistsAsync(key, token));
            Assert.Equal(LocalStorageNamespaceState.Open, await storage.GetNamespaceStateAsync(otherId, token));
            if (installFirst)
            {
                await using var installed = await storage.OpenReadAsync(key, token);
                using var reader = new StreamReader(installed);
                Assert.Equal("complete artifact", await reader.ReadToEndAsync(token));
            }
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ClosedNamespace_ShouldSurviveLostAcknowledgement_ProcessDeath_AndPayloadDeletion()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-restart-");
        var datasetId = Guid.NewGuid();
        var storage = new LocalFileStorage(directory.FullName);
        var originalKey = $"datasets/{datasetId:N}/published.parquet";

        try
        {
            await using (var original = new MemoryStream("published artifact"u8.ToArray()))
            {
                await storage.SaveAsync(originalKey, original, token);
            }
            await using (var closing = Child.Start(directory.FullName, datasetId, "close", "persisted"))
            {
                await closing.ExpectAsync("prepared", token);
                await closing.ContinueAsync(token);
                await closing.ExpectAsync("locked", token);
                closing.Kill();
                await closing.WaitForExitAsync(token);
            }

            var restarted = new LocalFileStorage(directory.FullName);
            Assert.Equal(LocalStorageNamespaceState.Closed, await restarted.GetNamespaceStateAsync(datasetId, token));
            await restarted.CloseNamespaceAsync(datasetId, token);
            await using (var published = await restarted.OpenReadAsync(originalKey, token))
            using (var reader = new StreamReader(published))
            {
                Assert.Equal("published artifact", await reader.ReadToEndAsync(token));
            }
            await restarted.DeleteDirectoryAsync($"datasets/{datasetId:N}", token);

            await using var late = Child.Start(directory.FullName, datasetId, "install");
            await late.ExpectAsync("prepared", token);
            await late.ContinueAsync(token);
            await late.ExpectAsync("rejected", token);
            await late.ExpectSuccessAsync(token);

            Assert.False(Directory.Exists(Path.Combine(directory.FullName, "datasets", datasetId.ToString("N"))));
            Assert.Equal(LocalStorageNamespaceState.Closed, await restarted.GetNamespaceStateAsync(datasetId, token));
            await using var input = new MemoryStream("late source"u8.ToArray());
            await Assert.ThrowsAsync<StorageNamespaceClosedException>(() =>
                restarted.SaveAsync($"datasets/{datasetId:N}/source.csv", input, token)
            );
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Staging_ShouldNotBlockClosure_WhileItsSourceCopyIsStillRunning()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-copy-");
        var datasetId = Guid.NewGuid();
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using var copying = Child.Start(directory.FullName, datasetId, "copy");
            await copying.ExpectAsync("copying", token);

            await storage.CloseNamespaceAsync(datasetId, token);

            await copying.ContinueAsync(token);
            await copying.ExpectAsync("rejected", token);
            await copying.ExpectSuccessAsync(token);
            Assert.False(await storage.ExistsAsync($"datasets/{datasetId:N}/result.parquet", token));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory.FullName, ".aperture-storage", "staging")));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("AP_NS_C")]
    [InlineData("AP_NS_O1C")]
    [InlineData("damaged!")]
    public async Task IncompleteState_ShouldBlockInstallation_AndAllowAnIdempotentClosure(string state)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-unknown-");
        var datasetId = Guid.NewGuid();
        var storage = new LocalFileStorage(directory.FullName);
        var originalKey = $"datasets/{datasetId:N}/published.parquet";

        try
        {
            Assert.Equal(LocalStorageNamespaceState.Uninitialized, await storage.GetNamespaceStateAsync(datasetId, token));
            await using (var original = new MemoryStream("published"u8.ToArray()))
            {
                await storage.SaveAsync(originalKey, original, token);
            }
            var marker = Path.Combine(directory.FullName, ".aperture-storage", "namespaces", datasetId.ToString("N"));
            await File.WriteAllTextAsync(marker, state, token);
            var restarted = new LocalFileStorage(directory.FullName);
            Assert.Equal(LocalStorageNamespaceState.Unknown, await restarted.GetNamespaceStateAsync(datasetId, token));
            await using var content = new MemoryStream("unpublished"u8.ToArray());
            await using var staged = await restarted.StageAsync(content, token);

            await Assert.ThrowsAsync<IOException>(() => restarted.InstallAsync(
                staged,
                $"datasets/{datasetId:N}/late.parquet",
                token
            ));

            Assert.True(await restarted.ExistsAsync(originalKey, token));
            Assert.False(await restarted.ExistsAsync($"datasets/{datasetId:N}/late.parquet", token));
            await restarted.CloseNamespaceAsync(datasetId, token);
            await restarted.CloseNamespaceAsync(datasetId, token);
            Assert.Equal(LocalStorageNamespaceState.Closed, await restarted.GetNamespaceStateAsync(datasetId, token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/rooted")]
    [InlineData("C:/rooted")]
    [InlineData(".aperture-storage/namespaces/marker")]
    [InlineData("datasets/{id}/../other/file")]
    [InlineData("datasets/{id}/file:stream")]
    [InlineData("datasets/{id}/file.")]
    [InlineData("datasets/{id}/file ")]
    [InlineData("datasets/{id}")]
    [InlineData("DATASETS/{id}/file")]
    [InlineData("datasets/not-a-guid/file")]
    [InlineData("APERT~1/namespaces/marker")]
    [InlineData("datasets/{id}/NUL")]
    [InlineData("tests/CON.txt")]
    [InlineData("dataſets/{id}/file")]
    [InlineData("datasets\\{id}\\file")]
    public async Task Installation_ShouldRejectNamespaceAndPathAliases(string template)
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-path-");
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using var input = new MemoryStream("private staged data"u8.ToArray());
            await using var staged = await storage.StageAsync(input, token);
            var key = template.Replace("{id}", Guid.NewGuid().ToString("N"), StringComparison.Ordinal);

            await Assert.ThrowsAsync<ArgumentException>(() => storage.InstallAsync(staged, key, token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StagedFile_ShouldNotBeInstalledThroughAnotherStorageInstance()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-owner-");
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using var input = new MemoryStream("private staged data"u8.ToArray());
            await using var staged = await storage.StageAsync(input, token);
            var another = new LocalFileStorage(directory.FullName);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                another.InstallAsync(staged, $"datasets/{Guid.NewGuid():N}/file", token)
            );
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task LostInstallationAcknowledgement_ShouldPreserveTheActuallyInstalledArtifact()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-namespace-install-ack-");
        var fault = new InstallationAcknowledgementFault();
        var storage = new LocalFileStorage(directory.FullName, fault);
        var key = $"datasets/{Guid.NewGuid():N}/result.parquet";

        try
        {
            await using var source = new MemoryStream("installed artifact"u8.ToArray());

            var exception = await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(key, source, token));

            Assert.Same(fault.Failure, exception);
            var restarted = new LocalFileStorage(directory.FullName);
            await using var installed = await restarted.OpenReadAsync(key, token);
            using var reader = new StreamReader(installed);
            Assert.Equal("installed artifact", await reader.ReadToEndAsync(token));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory.FullName, ".aperture-storage", "staging")));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task SharedReaders_ShouldRunConcurrently_AndDelayNamespaceClosureAcrossProcesses()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-shared-read-");
        var datasetId = Guid.NewGuid();
        var key = $"datasets/{datasetId:N}/result.parquet";
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using (var input = new MemoryStream("published"u8.ToArray()))
            {
                await storage.SaveAsync(key, input, token);
            }

            await using var first = Child.Start(directory.FullName, datasetId, "read");
            await using var second = Child.Start(directory.FullName, datasetId, "read");
            await using var closer = Child.Start(directory.FullName, datasetId, "close");
            await first.ExpectAsync("prepared", token);
            await second.ExpectAsync("prepared", token);
            await closer.ExpectAsync("prepared", token);

            await first.ContinueAsync(token);
            await first.ExpectAsync("reading", token);
            await second.ContinueAsync(token);
            await second.ExpectAsync("reading", token);
            await closer.ContinueAsync(token);
            await closer.ExpectAsync("blocked", token);

            await first.ContinueAsync(token);
            await first.ExpectAsync("read-completed", token);
            await first.ExpectSuccessAsync(token);
            await second.ContinueAsync(token);
            await second.ExpectAsync("read-completed", token);
            await second.ExpectSuccessAsync(token);
            await closer.ExpectAsync("closed", token);
            await closer.ExpectSuccessAsync(token);

            Assert.Equal(LocalStorageNamespaceState.Closed, await storage.GetNamespaceStateAsync(datasetId, token));
            await Assert.ThrowsAsync<StorageNamespaceClosedException>(() =>
                storage.ExecuteWithReadLeaseAsync(key, (_, _) => Task.FromResult(true), token));
            await storage.DeleteDirectoryAsync($"datasets/{datasetId:N}", token);
            Assert.False(await storage.ExistsAsync(key, token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ReaderProcessDeath_ShouldReleaseNamespaceAndAllowClosure()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-read-death-");
        var datasetId = Guid.NewGuid();
        var key = $"datasets/{datasetId:N}/result.parquet";
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using (var input = new MemoryStream("published"u8.ToArray()))
            {
                await storage.SaveAsync(key, input, token);
            }
            await using var reader = Child.Start(directory.FullName, datasetId, "read");
            await using var closer = Child.Start(directory.FullName, datasetId, "close");
            await reader.ExpectAsync("prepared", token);
            await closer.ExpectAsync("prepared", token);
            await reader.ContinueAsync(token);
            await reader.ExpectAsync("reading", token);
            await closer.ContinueAsync(token);
            await closer.ExpectAsync("blocked", token);

            reader.Kill();
            await reader.WaitForExitAsync(token);
            await closer.ExpectAsync("closed", token);
            await closer.ExpectSuccessAsync(token);
            await storage.DeleteDirectoryAsync($"datasets/{datasetId:N}", token);
            Assert.False(await storage.ExistsAsync(key, token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledRead_ShouldReleaseNamespaceLease()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-read-cancellation-");
        var datasetId = Guid.NewGuid();
        var key = $"datasets/{datasetId:N}/result.parquet";
        var storage = new LocalFileStorage(directory.FullName);

        try
        {
            await using (var input = new MemoryStream("published"u8.ToArray()))
            {
                await storage.SaveAsync(key, input, token);
            }
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            var leaseHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = storage.ExecuteWithReadLeaseAsync(
                key,
                async (_, ct) =>
                {
                    leaseHeld.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return true;
                },
                cancelled.Token
            );

            await leaseHeld.Task.WaitAsync(token);
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            await storage.CloseNamespaceAsync(datasetId, token);
            Assert.Equal(LocalStorageNamespaceState.Closed, await storage.GetNamespaceStateAsync(datasetId, token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyRead_ShouldInitializeMissingMarker_ButNeverReopenClosedOrUnknownNamespace()
    {
        using var deadline = CreateDeadline();
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("aperture-read-legacy-");
        var datasetId = Guid.NewGuid();
        var key = $"datasets/{datasetId:N}/result.parquet";
        var storage = new LocalFileStorage(directory.FullName);
        var path = Path.Combine(directory.FullName, "datasets", datasetId.ToString("N"), "result.parquet");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "legacy payload", token);
            var actual = await storage.ExecuteWithReadLeaseAsync(
                key,
                (file, _) => Task.FromResult(File.ReadAllText(file)),
                token
            );
            Assert.Equal("legacy payload", actual);
            Assert.Equal(LocalStorageNamespaceState.Open, await storage.GetNamespaceStateAsync(datasetId, token));

            await storage.CloseNamespaceAsync(datasetId, token);
            await Assert.ThrowsAsync<StorageNamespaceClosedException>(() =>
                storage.ExecuteWithReadLeaseAsync(key, (_, _) => Task.FromResult(true), token));

            var marker = Path.Combine(directory.FullName, ".aperture-storage", "namespaces", datasetId.ToString("N"));
            await File.WriteAllTextAsync(marker, "invalid!", token);
            await Assert.ThrowsAsync<IOException>(() =>
                storage.ExecuteWithReadLeaseAsync(key, (_, _) => Task.FromResult(true), token));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private sealed class InstallationAcknowledgementFault : ILogger<LocalFileStorage>
    {
        public IOException Failure { get; } = new("Lost acknowledgement after the actual installation.");

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).EndsWith("file installation completed.", StringComparison.Ordinal))
            {
                throw Failure;
            }
        }
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private sealed class Child : IAsyncDisposable
    {
        private readonly Process _process;

        private Child(Process process)
        {
            _process = process;
        }

        public int Id => _process.Id;

        public static Child Start(string root, Guid datasetId, string operation, string gate = "none")
        {
            var process = new Process();
            try
            {
                process.StartInfo = new ProcessStartInfo("dotnet");
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AnalyticDashboard.StorageTestHost.dll"));
                process.StartInfo.ArgumentList.Add(operation);
                process.StartInfo.ArgumentList.Add(root);
                process.StartInfo.ArgumentList.Add(datasetId.ToString("N"));
                process.StartInfo.ArgumentList.Add(gate);
                process.StartInfo.ArgumentList.Add("result.parquet");
                Assert.True(process.Start());
                return new Child(process);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        public async Task ExpectAsync(string expected, CancellationToken cancellationToken)
        {
            Assert.Equal(expected, await _process.StandardOutput.ReadLineAsync(cancellationToken));
        }

        public async Task ContinueAsync(CancellationToken cancellationToken)
        {
            await _process.StandardInput.WriteLineAsync("continue".AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public async Task ExpectSuccessAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken);
            Assert.Equal("", await _process.StandardError.ReadToEndAsync(cancellationToken));
            Assert.Equal(0, _process.ExitCode);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill() => _process.Kill(entireProcessTree: true);

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                Kill();
                await _process.WaitForExitAsync(CancellationToken.None);
            }
            _process.Dispose();
        }
    }
}
