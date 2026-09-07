namespace AnalyticDashboard.Application.Datasets.GetDatasetById;

public abstract record GetDatasetByIdResult
{
    public sealed record Found(
        Guid Id,
        string Name,
        DateTime CreatedAtUtc,
        CurrentVersion? Version
    ) : GetDatasetByIdResult;

    public sealed record NotFound
        : GetDatasetByIdResult;

    public sealed record CurrentVersion(
        Guid Id,
        int VersionNumber,
        string OriginalFileName,
        int RowCount,
        int ColumnCount
    );
}
