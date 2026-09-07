using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class ImportJobConfiguration
    : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(
        EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs");

        builder.HasKey(job => job.Id);

        builder.Property(job => job.DatasetVersionId)
            .IsRequired();

        builder.Property(job => job.SourceFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(job => job.SourceStorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(job => job.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(job => job.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(job => job.CreatedAtUtc)
            .IsRequired();

        builder.Property(job => job.StartedAtUtc);

        builder.Property(job => job.CompletedAtUtc);

        builder
            .HasOne<DatasetVersion>()
            .WithMany()
            .HasForeignKey(job => job.DatasetVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(job => job.DatasetVersionId)
            .IsUnique();

        builder
            .HasIndex(job => new
            {
                job.Status,
                job.CreatedAtUtc,
                job.Id
            });
    }
}
