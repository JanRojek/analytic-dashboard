using System.Globalization;
using AnalyticDashboard.Application.Datasets.GetDatasetProfile;
using AnalyticDashboard.Application.Profiling;
using CsvHelper;
using CsvHelper.Configuration;

namespace AnalyticDashboard.Infrastructure.Services.Profiling;

public sealed class CsvDatasetProfileReader : IDatasetProfileReader
{
    public async Task<GetDatasetProfileResponse> ReadProfileAsync(
        Guid datasetId,
        string name,
        string originalFileName,
        string storedPath,
        int rowCount,
        int columnCount,
        CancellationToken cancellationToken)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using var reader = new StreamReader(storedPath);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            throw new InvalidOperationException("CSV file has no header.");
        }

        csv.ReadHeader();

        var header = csv.HeaderRecord
            ?? throw new InvalidOperationException("CSV file has no header record.");

        var stats = header
            .Select(column => new ColumnStats(column))
            .ToDictionary(x => x.Name);

        var preview = new List<IReadOnlyDictionary<string, string?>>();

        while (await csv.ReadAsync())
        {
            var row = new Dictionary<string, string?>();

            foreach (var column in header)
            {
                var value = csv.GetField(column);

                stats[column].AddValue(value);

                if (preview.Count < 10)
                {
                    row[column] = value;
                }
            }

            if (preview.Count < 10)
            {
                preview.Add(row);
            }
        }

        var columns = header
            .Select(h => stats[h].ToProfile())
            .ToList();

        return new GetDatasetProfileResponse(
            datasetId,
            name,
            originalFileName,
            rowCount,
            columnCount,
            columns,
            preview
        );
    }

    private sealed class ColumnStats
    {
        public string Name { get; }

        public int NullCount { get; private set; }

        public bool CanBeNumber { get; private set; } = true;
        public bool CanBeDate { get; private set; } = true;

        public double? MinNumber { get; private set; }
        public double? MaxNumber { get; private set; }
        public double Sum { get; private set; }
        public int NumberCount { get; private set; }

        public ColumnStats(string name)
        {
            Name = name;
        }

        public void AddValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                NullCount++;
                return;
            }

            if (double.TryParse(
                    value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                MinNumber = MinNumber is null ? number : Math.Min(MinNumber.Value, number);
                MaxNumber = MaxNumber is null ? number : Math.Max(MaxNumber.Value, number);
                Sum += number;
                NumberCount++;
            }
            else
            {
                CanBeNumber = false;
            }

            if (!DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                CanBeDate = false;
            }
        }
        
        public ColumnProfile ToProfile()
        {
            if (CanBeNumber && NumberCount > 0)
            {
                return new ColumnProfile(
                    Name,
                    "number",
                    NullCount,
                    MinNumber?.ToString(CultureInfo.InvariantCulture),
                    MaxNumber?.ToString(CultureInfo.InvariantCulture),
                    NumberCount == 0 ? null : Math.Round(Sum / NumberCount, 2)
                );
            }

            if (CanBeDate)
            {
                return new ColumnProfile(
                    Name,
                    "date",
                    NullCount,
                    null,
                    null,
                    null
                );
            }

            return new ColumnProfile(
                Name,
                "text",
                NullCount,
                null,
                null,
                null
            );
        }
    }
}