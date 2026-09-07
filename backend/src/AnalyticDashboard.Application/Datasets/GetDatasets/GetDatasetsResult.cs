namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public abstract record GetDatasetsResult
{
    public sealed record Success(
        IReadOnlyList<Item> Items
    ) : GetDatasetsResult;

    public sealed record NotFound
        : GetDatasetsResult;

    public sealed record Item(
        Guid Id,
        string Name,
        DateTime CreatedAtUtc,
        CurrentVersion? Version
    );

    public sealed record CurrentVersion(
        Guid Id,
        int VersionNumber,
        string OriginalFileName,
        int RowCount,
        int ColumnCount
    );
}
