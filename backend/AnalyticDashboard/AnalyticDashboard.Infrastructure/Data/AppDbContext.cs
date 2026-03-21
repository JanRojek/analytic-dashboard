using Microsoft.EntityFrameworkCore;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Dataset> Datasets => Set<Dataset>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}