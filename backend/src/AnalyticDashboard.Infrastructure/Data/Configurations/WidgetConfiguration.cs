using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("Widgets");

        builder.HasKey(widget => widget.Id);

        builder.Property(widget => widget.DashboardId)
            .IsRequired();

        builder.Property(widget => widget.DatasetId)
            .IsRequired();

        builder.Property(widget => widget.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(widget => widget.Title)
            .IsRequired()
            .HasMaxLength(Widget.MaxTitleLength);

        builder.Property(widget => widget.GroupByColumn)
            .IsRequired()
            .HasMaxLength(Widget.MaxGroupByColumnLength);

        builder.Property(widget => widget.MeasureColumn)
            .IsRequired()
            .HasMaxLength(Widget.MaxMeasureColumnLength);

        builder.Property(widget => widget.Aggregation)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(widget => widget.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<Dashboard>()
            .WithMany()
            .HasForeignKey(widget => widget.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Dataset>()
            .WithMany()
            .HasForeignKey(widget => widget.DatasetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(widget => widget.DashboardId);

        builder.HasIndex(widget => widget.DatasetId);
    }
}
