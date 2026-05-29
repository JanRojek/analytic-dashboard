namespace AnalyticDashboard.Infrastructure.Services.Csv;

public sealed record CsvReadResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows
);