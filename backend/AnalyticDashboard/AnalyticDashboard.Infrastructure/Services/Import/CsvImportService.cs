using AnalyticDashboard.Application.Import;
using AnalyticDashboard.Infrastructure.Services.Csv;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class CsvImportService : ICsvImportService
{
    private readonly CsvDatasetReader _csvDatasetReader;

    public CsvImportService(CsvDatasetReader csvDatasetReader)
    {
        _csvDatasetReader = csvDatasetReader;
    }
    
    public async Task<CsvImportResult> ImportAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        ValidateExtension(fileName);

        var datasetId = Guid.NewGuid();
        var storedFilePath = await SaveFileAsync(fileStream, datasetId, cancellationToken);

        try
        {
            var csvData = await _csvDatasetReader.ReadAsync(
                storedFilePath,
                cancellationToken);

            return new CsvImportResult(
                datasetId,
                fileName,
                storedFilePath,
                csvData.Rows.Count,
                csvData.Headers.Count
            );
        }
        catch
        {
            if (File.Exists(storedFilePath))
            {
                File.Delete(storedFilePath);
            }

            throw;
        }
    }

    private static void ValidateExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File is not a .csv file.", nameof(fileName));
        }
    }

    private static async Task<string> SaveFileAsync(
        Stream fileStream,
        Guid datasetId,
        CancellationToken cancellationToken)
    {
        var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "storage", "datasets");
        Directory.CreateDirectory(storagePath);
        
        var storedFileName = $"{datasetId}.csv";
        var storedFilePath = Path.Combine(storagePath, storedFileName);
        
        await using var outputStream = new FileStream(
            storedFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await fileStream.CopyToAsync(outputStream, cancellationToken);

        return storedFilePath;
    }
}