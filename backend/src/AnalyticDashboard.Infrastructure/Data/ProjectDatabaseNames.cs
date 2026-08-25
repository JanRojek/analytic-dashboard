namespace AnalyticDashboard.Infrastructure.Data;

internal static class ProjectDatabaseNames
{
    public const string OwnerNameUniqueIndex =
        "IX_projects_OwnerId_Name";

    public const string OwnerCreatedAtUtcIdIndex =
        "IX_projects_OwnerId_CreatedAtUtc_Id";

    public const string OwnerIdNotEmptyCheck =
        "CK_projects_OwnerId_NotEmpty";

    public const string NameNotBlankCheck =
        "CK_projects_Name_NotBlank";

    public const string NameMaxLengthCheck =
        "CK_projects_Name_MaxLength";

    public const string NameTrimmedCheck =
        "CK_projects_Name_Trimmed";
}
