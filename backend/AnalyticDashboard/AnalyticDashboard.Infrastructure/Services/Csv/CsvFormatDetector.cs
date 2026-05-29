namespace AnalyticDashboard.Infrastructure.Services.Csv;

public sealed class CsvFormatDetector
{
    public CsvFormatDetectionResult Detect(string storedFilePath)
    {
        var sampleLines = ReadSampleLines(storedFilePath);
        return DetectDelimiter(sampleLines);
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
}