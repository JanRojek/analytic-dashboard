using System.Security.Cryptography;
using System.Text;

using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Infrastructure.Services.Import;

using DuckDB.NET.Data;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class DuckDbImportResourceTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public DuckDbImportResourceTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PrivateDatabases_ShouldHaveIndependentBudgets_WhileDuplicateConnectionsShareGlobalSettings()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Directory.CreateTempSubdirectory("aperture-duckdb-budgets-");
        try
        {
            var firstSettings = new DuckDBConnectionStringBuilder
            {
                DataSource = DuckDBConnectionStringBuilder.InMemoryDataSource,
                ["threads"] = 1,
                ["memory_limit"] = "64MiB",
                ["max_temp_directory_size"] = "32MiB",
                ["temp_directory"] = Path.Combine(workspace.FullName, "first")
            };
            var secondSettings = new DuckDBConnectionStringBuilder
            {
                DataSource = DuckDBConnectionStringBuilder.InMemoryDataSource,
                ["threads"] = 2,
                ["memory_limit"] = "96MiB",
                ["max_temp_directory_size"] = "64MiB",
                ["temp_directory"] = Path.Combine(workspace.FullName, "second")
            };
            await using var first = new DuckDBConnection(firstSettings.ConnectionString);
            await using var second = new DuckDBConnection(secondSettings.ConnectionString);
            await first.OpenAsync(cancellationToken);
            await second.OpenAsync(cancellationToken);
            await using var firstCommand = first.CreateCommand();
            await using var secondCommand = second.CreateCommand();
            firstCommand.CommandText = """
                SELECT COUNT(*) FROM duckdb_settings()
                WHERE name IN ('threads', 'memory_limit', 'max_temp_directory_size', 'temp_directory')
                  AND scope = 'GLOBAL'
                """;
            Assert.Equal(4L, await firstCommand.ExecuteScalarAsync(cancellationToken));
            firstCommand.CommandText = "SELECT current_setting('memory_limit')";
            Assert.Equal("64.0 MiB", await firstCommand.ExecuteScalarAsync(cancellationToken));
            secondCommand.CommandText = "SELECT current_setting('memory_limit')";
            Assert.Equal("96.0 MiB", await secondCommand.ExecuteScalarAsync(cancellationToken));
            firstCommand.CommandText = "SET threads = 3; CREATE TABLE private_import AS SELECT 1 AS value";
            await firstCommand.ExecuteNonQueryAsync(cancellationToken);
            secondCommand.CommandText = "SELECT current_setting('threads')";
            Assert.Equal(2L, await secondCommand.ExecuteScalarAsync(cancellationToken));
            secondCommand.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'private_import'";
            Assert.Equal(0L, await secondCommand.ExecuteScalarAsync(cancellationToken));

            await using var duplicate = first.Duplicate();
            await duplicate.OpenAsync(cancellationToken);
            await using var duplicateCommand = duplicate.CreateCommand();
            duplicateCommand.CommandText = "SELECT current_setting('threads')";
            Assert.Equal(3L, await duplicateCommand.ExecuteScalarAsync(cancellationToken));
            duplicateCommand.CommandText = "SELECT value FROM private_import";
            Assert.Equal(1, await duplicateCommand.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            Directory.Delete(workspace.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Importer_ShouldApplyItsMemoryAndSpillBudgets_BeforeConvertingTheSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var directory = $"tests/import-budgets/{Guid.NewGuid():N}";
        var sourceKey = directory + "/source.csv";
        try
        {
            using var bytes = new MemoryStream();
            await using (var writer = new StreamWriter(bytes, Encoding.UTF8, leaveOpen: true))
            {
                await writer.WriteLineAsync("Id,Payload".AsMemory(), cancellationToken);
                for (var index = 0; index < 50000; index++)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(index)));
                    var payload = string.Concat(Enumerable.Repeat(hash, 16));
                    await writer.WriteLineAsync($"{index},{payload}".AsMemory(), cancellationToken);
                }
                await writer.FlushAsync(cancellationToken);
            }
            bytes.Position = 0;
            await storage.SaveAsync(sourceKey, bytes, cancellationToken);
            var limited = new DuckDbCsvImporter(
                storage,
                NullLogger<DuckDbCsvImporter>.Instance,
                Options.Create(new ImportJobOptions
                {
                    DuckDbMemoryLimitMb = 16,
                    DuckDbMaxTempDirectorySizeMb = 0
                })
            );

            await Assert.ThrowsAsync<DuckDBException>(() =>
                limited.ImportAsync(sourceKey, directory + "/limited", cancellationToken)
            );

            var normal = scope.ServiceProvider.GetRequiredService<DuckDbCsvImporter>();
            var result = await normal.ImportAsync(sourceKey, directory + "/normal", cancellationToken);
            Assert.Equal(50000, result.RowCount);
            Assert.Equal(2, result.ColumnCount);
            Assert.True(await storage.ExistsAsync(result.StorageKey, cancellationToken));
        }
        finally
        {
            await storage.DeleteDirectoryAsync(directory, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("MaxConcurrentImports", "0")]
    [InlineData("MaxConcurrentImports", "9")]
    [InlineData("DuckDbThreads", "0")]
    [InlineData("DuckDbThreads", "5")]
    [InlineData("DuckDbMemoryLimitMb", "15")]
    [InlineData("DuckDbMemoryLimitMb", "4097")]
    [InlineData("DuckDbMaxTempDirectorySizeMb", "-1")]
    [InlineData("DuckDbMaxTempDirectorySizeMb", "16385")]
    public void UnsafeResourceOptions_ShouldBeRejectedByProductionValidation(string name, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["ImportJobs:WorkerEnabled"] = "false",
                ["StorageCleanup:WorkerEnabled"] = "false",
                [$"ImportJobs:{name}"] = value
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
}
