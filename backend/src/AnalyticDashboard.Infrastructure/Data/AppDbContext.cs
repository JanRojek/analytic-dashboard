using AnalyticDashboard.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Infrastructure.Storage;
using Npgsql;

namespace AnalyticDashboard.Infrastructure.Data;

public class AppDbContext : IdentityUserContext<ApplicationUser, Guid>, IUnitOfWork
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetVersion> DatasetVersions => Set<DatasetVersion>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<StorageCleanupJob> StorageCleanupJobs => Set<StorageCleanupJob>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityUserClaim<Guid>>()
            .ToTable("user_claims");

        modelBuilder.Entity<IdentityUserLogin<Guid>>()
            .ToTable("user_logins");

        modelBuilder.Entity<IdentityUserToken<Guid>>()
            .ToTable("user_tokens");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly
        );

        modelBuilder.HasPostgresExtension("citext");
    }

    async Task<int> IUnitOfWork.SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OptimisticConcurrencyException(ex.Message, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
               {
                   SqlState: PostgresErrorCodes.UniqueViolation,
                   TableName: "Widgets",
                   ConstraintName: "PK_Widgets"
               })
        {
            throw new WidgetIdConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
               {
                   SqlState: PostgresErrorCodes.ForeignKeyViolation,
                   TableName: "Widgets",
                   ConstraintName: "FK_Widgets_datasets_DatasetId"
               })
        {
            throw new MissingReferenceException(nameof(Dataset), ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
               {
                   SqlState: PostgresErrorCodes.ForeignKeyViolation,
                   TableName: "Widgets",
                   ConstraintName: "FK_Widgets_Dashboards_DashboardId"
               })
        {
            throw new MissingReferenceException(nameof(Dashboard), ex);
        }
    }
}
