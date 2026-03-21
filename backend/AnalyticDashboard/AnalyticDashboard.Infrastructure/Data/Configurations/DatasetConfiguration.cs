using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> builder)
    {
        builder.ToTable("datasets");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(d => d.StoredPath).IsRequired().HasMaxLength(500);
        builder.Property(d => d.CreatedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.HasIndex(d => d.CreatedAtUtc);
        builder.Property(d => d.RowCount).IsRequired();
        builder.Property(d => d.ColumnCount).IsRequired();
    }
}