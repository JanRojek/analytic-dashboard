using Microsoft.EntityFrameworkCore;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<Project> Projects => Set<Project>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.HasPostgresExtension("citext");
    }
}
