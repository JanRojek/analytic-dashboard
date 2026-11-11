using AnalyticDashboard.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class StorageCleanupJobConfiguration
    : IEntityTypeConfiguration<StorageCleanupJob>
{
    public void Configure(EntityTypeBuilder<StorageCleanupJob> builder)
    {
        builder.ToTable("storage_cleanup_jobs");

        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .ValueGeneratedNever();

        builder.HasIndex(job => job.DatasetId)
            .IsUnique();

        builder.HasIndex(job => job.NextAttemptAtUtc);
    }
}
