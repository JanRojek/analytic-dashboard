using System.Text;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Import;

public sealed class DuckDbCsvImporterTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DuckDbCsvImporterTests(
        ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ImportAsync_ShouldConvertCsvToParquetAndReturnMetadata()
    {
        var importId = Guid.NewGuid();

        var sourceStorageKey =
            $"tests/duckdb/{importId:N}/source.csv";

        var resultStorageDirectory = $"tests/duckdb/{importId:N}/attempts";

        await using var scope =
            _fixture.Services.CreateAsyncScope();

        var fileStorage = scope.ServiceProvider
            .GetRequiredService<IFileStorage>();

        var importer = scope.ServiceProvider
            .GetRequiredService<DuckDbCsvImporter>();

        try
        {
            var csv = """
                Name,Amount,Country
                Alice,10,Poland
                Bob,20,Germany
                Charlie,30,Poland
                """;

            await using var sourceStream = new MemoryStream(
                Encoding.UTF8.GetBytes(csv)
            );

            await fileStorage.SaveAsync(
                sourceStorageKey,
                sourceStream,
                CancellationToken
            );

            var result = await importer.ImportAsync(
                sourceStorageKey,
                resultStorageDirectory,
                CancellationToken
            );

            Assert.StartsWith(resultStorageDirectory + "/", result.StorageKey);
            Assert.EndsWith(".parquet", result.StorageKey);

            Assert.Equal(
                3,
                result.RowCount
            );

            Assert.Equal(
                3,
                result.ColumnCount
            );

            var resultExists = await fileStorage.ExistsAsync(
                result.StorageKey,
                CancellationToken
            );

            Assert.True(
                resultExists
            );

            await using var parquetStream =
                await fileStorage.OpenReadAsync(
                    result.StorageKey,
                    CancellationToken
                );

            var parquetMagic = new byte[4];

            await parquetStream.ReadExactlyAsync(
                parquetMagic,
                CancellationToken
            );

            Assert.Equal(
                "PAR1",
                Encoding.ASCII.GetString(parquetMagic)
            );
        }
        finally
        {
            await fileStorage.DeleteDirectoryAsync(
                $"tests/duckdb/{importId:N}",
                CancellationToken
            );
        }
    }
}
