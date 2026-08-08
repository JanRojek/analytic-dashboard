namespace AnalyticDashboard.Infrastructure.Services.Csv;

public enum CsvDetectionStatus
{
    DelimiterDetected,
    SingleColumn,
    Ambiguous
}

public sealed record CsvFormatDetectionResult(
    CsvDetectionStatus Status,
    string? Delimiter
);