namespace AnalyticDashboard.Application.Import;

public interface ICsvImportService
{
    Task<CsvImportResult> ImportAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken
    );
}