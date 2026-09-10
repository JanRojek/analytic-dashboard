using AnalyticDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AnalyticDashboard.Infrastructure.Data.Configurations;

public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.ToTable("Dashboards");

        builder.HasKey(dashboard => dashboard.Id);

        builder.Property(dashboard => dashboard.ProjectId)
            .IsRequired();

        builder.Property(dashboard => dashboard.Name)
            .IsRequired()
            .HasMaxLength(Dashboard.MaxNameLength);

        builder.Property(dashboard => dashboard.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(dashboard => dashboard.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(dashboard => dashboard.ProjectId);
    }
}
