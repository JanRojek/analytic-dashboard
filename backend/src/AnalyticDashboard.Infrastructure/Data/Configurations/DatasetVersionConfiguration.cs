using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class DatasetVersionConfiguration
    : IEntityTypeConfiguration<DatasetVersion>
{
    public void Configure(
        EntityTypeBuilder<DatasetVersion> builder)
    {
        builder.ToTable("dataset_versions");

        builder.HasKey(version => version.Id);

        builder.Property(version => version.DatasetId)
            .IsRequired();

        builder.Property(version => version.VersionNumber)
            .IsRequired();

        builder.Property(version => version.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(version => version.StorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(version => version.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(version => version.RowCount);

        builder.Property(version => version.ColumnCount);

        builder.Property(version => version.CreatedAtUtc)
            .IsRequired();

        builder
            .HasOne<Dataset>()
            .WithMany()
            .HasForeignKey(version => version.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(version => new
            {
                version.DatasetId,
                version.VersionNumber
            })
            .IsUnique();

        builder
            .HasIndex(version => new
            {
                version.DatasetId,
                version.CreatedAtUtc,
                version.Id
            })
            .IsDescending(
                false,
                true,
                false
            );
    }
}
