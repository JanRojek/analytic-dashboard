using AnalyticDashboard.Application.Import;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Services.Csv;
using Microsoft.Extensions.Logging;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class CsvImportService : ICsvImportService
{
    private readonly IFileStorage _fileStorage;
    private readonly CsvDatasetReader _csvDatasetReader;
    private readonly ILogger<CsvImportService> _logger;

    public CsvImportService(
        IFileStorage fileStorage,
        CsvDatasetReader csvDatasetReader,
        ILogger<CsvImportService> logger)
    {
        _fileStorage = fileStorage;
        _csvDatasetReader = csvDatasetReader;
        _logger = logger;
    }

    public async Task<CsvImportResult> ImportAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        ValidateExtension(
            fileName
        );

        var storageKey =
            $"datasets/imports/{Guid.NewGuid():N}.csv";

        try
        {
            await _fileStorage.SaveAsync(
                storageKey,
                fileStream,
                cancellationToken
            );

            var csvData =
                await _csvDatasetReader.ReadAsync(
                    storageKey,
                    cancellationToken
                );

            return new CsvImportResult(
                fileName,
                storageKey,
                csvData.Rows.Count,
                csvData.Headers.Count
            );
        }
        catch
        {
            try
            {
                await _fileStorage.DeleteAsync(
                    storageKey,
                    CancellationToken.None
                );
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Failed to clean up storage object {StorageKey} after CSV import failure.",
                    storageKey
                );
            }

            throw;
        }
    }

    private static void ValidateExtension(
        string fileName)
    {
        var extension =
            Path.GetExtension(fileName);

        if (!string.Equals(
                extension,
                ".csv",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "File is not a .csv file.",
                nameof(fileName)
            );
        }
    }
}
