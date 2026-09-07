using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class DatasetConfiguration
    : IEntityTypeConfiguration<Dataset>
{
    public void Configure(
        EntityTypeBuilder<Dataset> builder)
    {
        builder.ToTable("datasets");

        builder.HasKey(dataset => dataset.Id);

        builder.Property(dataset => dataset.ProjectId)
            .IsRequired();

        builder.Property(dataset => dataset.CurrentVersionId);

        builder.Property(dataset => dataset.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(dataset => dataset.CreatedAtUtc)
            .IsRequired();

        builder
            .HasOne<Project>()
            .WithMany()
            .HasForeignKey(dataset => dataset.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<DatasetVersion>()
            .WithMany()
            .HasForeignKey(dataset => dataset.CurrentVersionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder
            .HasIndex(dataset => new
            {
                dataset.ProjectId,
                dataset.CreatedAtUtc,
                dataset.Id
            })
            .IsDescending(
                false,
                true,
                false
            );
    }
}
