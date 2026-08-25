using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    private const string NameTrimCharacters =
        @"E' \u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680" +
        @"\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A" +
        @"\u2028\u2029\u202F\u205F\u3000'";

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable(
            "projects",
            table =>
            {
                table.HasCheckConstraint(
                    ProjectDatabaseNames.OwnerIdNotEmptyCheck,
                    "\"OwnerId\" <> '00000000-0000-0000-0000-000000000000'::uuid"
                );

                table.HasCheckConstraint(
                    ProjectDatabaseNames.NameNotBlankCheck,
                    $"btrim(\"Name\"::text, {NameTrimCharacters}) <> ''"
                );

                table.HasCheckConstraint(
                    ProjectDatabaseNames.NameMaxLengthCheck,
                    $"char_length(\"Name\"::text) <= {Project.MaxNameLength}"
                );

                table.HasCheckConstraint(
                    ProjectDatabaseNames.NameTrimmedCheck,
                    $"\"Name\"::text = btrim(\"Name\"::text, {NameTrimCharacters})"
                );
            }
        );

        builder.HasKey(project => project.Id);

        builder.Property(project => project.OwnerId)
            .IsRequired();

        builder.Property(project => project.Name)
            .HasColumnType("citext")
            .IsRequired()
            .HasMaxLength(Project.MaxNameLength);

        builder
            .HasIndex(project => new
            {
                project.OwnerId,
                project.Name
            })
            .IsUnique()
            .HasDatabaseName(
                ProjectDatabaseNames.OwnerNameUniqueIndex
            );

        builder
            .HasIndex(project => new
            {
                project.OwnerId,
                project.CreatedAtUtc,
                project.Id
            })
            .IsDescending(
                false,
                true,
                false
            )
            .HasDatabaseName(
                ProjectDatabaseNames.OwnerCreatedAtUtcIdIndex
            );
    }
}
