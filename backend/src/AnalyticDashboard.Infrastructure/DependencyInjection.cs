using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AnalyticDashboard.Application.Profiling;
using AnalyticDashboard.Infrastructure.Auth.Email;
using AnalyticDashboard.Infrastructure.Identity;
using AnalyticDashboard.Infrastructure.Services.Profiling;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Storage;
using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Infrastructure.Services.Analytics;

namespace AnalyticDashboard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing connection string 'Default'.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IDatasetRepository, DatasetRepository>();

        services.AddScoped<IDatasetVersionRepository, DatasetVersionRepository>();

        services.AddScoped<IImportJobRepository, ImportJobRepository>();

        services.AddScoped<IPendingDatasetImportRepository, PendingDatasetImportRepository>();

        services.AddScoped<IFileStorage, LocalFileStorage>();

        services.AddScoped<IDatasetProfileReader, DuckDbDatasetProfileReader>();

        services.AddScoped<IDashboardRepository, DashboardRepository>();

        services.AddScoped<IWidgetRepository, WidgetRepository>();

        services.AddScoped<IProjectRepository, ProjectRepository>();

        services.AddScoped<IUserAccountService, UserAccountService>();

        services.AddScoped<IUserAccountTokenService, UserAccountTokenService>();

        services.AddSingleton<IEmailConfirmationLinkBuilder, EmailConfirmationLinkBuilder>();

        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));

        services.AddScoped<DuckDbCsvImporter>();

        services.AddScoped<ImportJobProcessor>();

        services.AddScoped<IDatasetQueryReader, DuckDbDatasetQueryReader>();

        services.AddScoped<IEmailSender, SmtpEmailSender>();

        var workerEnabled = configuration.GetValue(
            "ImportJobs:WorkerEnabled",
            true
        );

        if (workerEnabled)
        {
            services.AddHostedService<ImportJobWorker>();
        }

        return services;
    }
}
