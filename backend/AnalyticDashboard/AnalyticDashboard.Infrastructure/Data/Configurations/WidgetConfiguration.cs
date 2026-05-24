using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("Widgets");
        
        builder.HasKey(w => w.Id);

        builder.Property(w => w.DashboardId)
            .IsRequired();

        builder.Property(w => w.Type)
            .IsRequired()
            .HasConversion<string>();
            
        builder.Property(w => w.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.XColumn)
            .HasMaxLength(200);
        
        builder.Property(w => w.YColumn)
            .HasMaxLength(200);
        
        builder.Property(w => w.Aggregation)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(w => w.CreatedAtUtc)
            .IsRequired();
        
        builder.HasOne(w => w.Dashboard)
            .WithMany()
            .HasForeignKey(w => w.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}