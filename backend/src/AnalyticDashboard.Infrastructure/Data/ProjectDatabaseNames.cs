namespace AnalyticDashboard.Infrastructure.Data;

internal static class ProjectDatabaseNames
{
    public const string OwnerNameUniqueIndex =
        "IX_projects_OwnerId_Name";

    public const string OwnerCreatedAtUtcIdIndex =
        "IX_projects_OwnerId_CreatedAtUtc_Id";
}
