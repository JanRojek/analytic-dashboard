using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

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
