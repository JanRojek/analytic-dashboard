using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.ToTable("Dashboards");
        
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.DatasetId)
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired();
        
        builder.HasOne(d => d.Dataset)
            .WithMany()
            .HasForeignKey(d => d.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}