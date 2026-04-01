using System.Globalization;
using AnalyticDashboard.Application.Import;
using CsvHelper;
using CsvHelper.Configuration;

namespace AnalyticDashboard.Infrastructure.Services.Import;

public sealed class CsvImportService : ICsvImportService
{
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
            var sampleLines = ReadSampleLines(storedFilePath);
            var detectionResult = DetectDelimiter(sampleLines);

            var delimiter = detectionResult.Status switch
            {
                CsvDetectionStatus.DelimiterDetected => detectionResult.Delimiter!,
                CsvDetectionStatus.SingleColumn => "\u001F",
                CsvDetectionStatus.Ambiguous => GetDelimiterCandidates(sampleLines)
                    .First()
                    .ToString(),
                _ => throw new InvalidOperationException("Unsupported CSV detection status.")
            };

            var structure = await ReadCsvStructureAsync(
                storedFilePath,
                delimiter,
                cancellationToken);

            return new CsvImportResult(
                datasetId,
                fileName,
                storedFilePath,
                structure.RowCount,
                structure.ColumnCount
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

    private static List<string> ReadSampleLines(
        string storedFilePath,
        int maxLines = 20)
    {
        var lines = File.ReadLines(storedFilePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(maxLines)
            .ToList();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("CSV file has no data.");
        }

        return lines;
    }
    
    private static List<char> GetDelimiterCandidates(IReadOnlyList<string> lines)
    {
        return lines.SelectMany(line => line)
            .Where(c => 
                !char.IsWhiteSpace(c) &&
                !char.IsLetterOrDigit(c) &&
                !char.IsControl(c) &&
                c != '"')
            .Distinct()
            .ToList();
    }
    
    private static int CountDelimiterOutsideQuotes(string line, char candidate)
    {
        var count = 0;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && line[i] == candidate)
            {
                count++;
            }
        }
        
        return count;
    }

    private static CsvFormatDetectionResult DetectDelimiter(IReadOnlyList<string> lines)
    {
        var foundInconsistentCandidate = false;
        
        var candidates = GetDelimiterCandidates(lines);
        
        foreach (var candidate in candidates)
        {
            var delimiterCountsPerLine = new int[lines.Count];

            for (var i = 0; i < lines.Count; i++)
            {
                delimiterCountsPerLine[i] = CountDelimiterOutsideQuotes(lines[i], candidate);
            }

            var first = delimiterCountsPerLine[0];
            
            if (first > 0 && delimiterCountsPerLine.All(x => x == first))
            {
                return new CsvFormatDetectionResult(
                    CsvDetectionStatus.DelimiterDetected,
                    candidate.ToString());
            }

            var appearsInAnyLine = delimiterCountsPerLine.Any(x => x > 0);
            var sameCountInEveryLine = delimiterCountsPerLine.All(x => x == delimiterCountsPerLine[0]);

            if (appearsInAnyLine && !sameCountInEveryLine)
            {
                foundInconsistentCandidate = true;
            }
        }
        
        if (foundInconsistentCandidate)
        {
            return new CsvFormatDetectionResult(
                CsvDetectionStatus.Ambiguous,
                null);
        }
        
        return new CsvFormatDetectionResult(
            CsvDetectionStatus.SingleColumn,
            null);
    }

    private static async Task<CsvStructureInfo> ReadCsvStructureAsync(
        string storedFilePath,
        string delimiter,
        CancellationToken cancellationToken)
    {
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

        var header = csv.HeaderRecord;
        
        if (header == null || header.Length == 0)
        {
            throw new InvalidOperationException("CSV file has no header record.");
        }
        
        var columnCount = header.Length;
        
        var lineNumber = 1;
        var rowCount = 0;
        
        while (await csv.ReadAsync())
        {
            lineNumber++;
            
            var record = csv.Parser.Record;
            
            if (record == null || record.Length != columnCount)
            {
                throw new InvalidOperationException(
                    $"CSV has inconsistent number of columns at line {lineNumber}.");
            }

            rowCount++;
        }

        if (rowCount == 0)
        {
            throw new InvalidOperationException("CSV file contains no data rows.");
        }
        
        return new CsvStructureInfo(columnCount, rowCount);
    }
    
    private enum CsvDetectionStatus
    {
        DelimiterDetected,
        SingleColumn,
        Ambiguous
    }
    
    private sealed record CsvFormatDetectionResult(
        CsvDetectionStatus Status,
        string? Delimiter
    );

    private sealed record CsvStructureInfo(int ColumnCount, int RowCount);
}