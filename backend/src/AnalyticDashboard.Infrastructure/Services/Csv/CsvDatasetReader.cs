using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace AnalyticDashboard.Infrastructure.Services.Csv;

public sealed class CsvDatasetReader
{
    private readonly CsvFormatDetector _formatDetector;

    public CsvDatasetReader(CsvFormatDetector formatDetector)
    {
        _formatDetector = formatDetector;
    }

    public async Task<CsvReadResult> ReadAsync(
        string storedFilePath,
        CancellationToken cancellationToken)
    {
        var delimiter = ResolveDelimiter(storedFilePath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true
        };

        using var reader = new StreamReader(storedFilePath);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            throw new InvalidOperationException("CSV file has no header.");
        }

        csv.ReadHeader();

        var header = csv.HeaderRecord
            ?? throw new InvalidOperationException("CSV file has no header record.");

        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (await csv.ReadAsync())
        {
            var row = new Dictionary<string, string?>();

            foreach (var column in header)
            {
                row[column] = csv.GetField(column);
            }

            rows.Add(row);
        }

        return new CsvReadResult(
            header,
            rows
        );
    }

    private string ResolveDelimiter(string storedFilePath)
    {
        var detectionResult = _formatDetector.Detect(storedFilePath);

        return detectionResult.Status switch
        {
            CsvDetectionStatus.DelimiterDetected => detectionResult.Delimiter!,
            CsvDetectionStatus.SingleColumn => "\u001F",
            CsvDetectionStatus.Ambiguous => throw new InvalidOperationException("CSV delimiter is ambiguous."),
            _ => throw new InvalidOperationException("Unsupported CSV detection status.")
        };
    }
}