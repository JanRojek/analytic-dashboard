namespace AnalyticDashboard.Application.Datasets.GetDatasets;

public sealed record GetDatasetsItem(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    GetDatasetsItem.CurrentVersion? Version
)
{
    public sealed record CurrentVersion(
        Guid Id,
        int VersionNumber,
        string OriginalFileName,
        int RowCount,
        int ColumnCount
    );
}
