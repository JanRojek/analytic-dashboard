using System.ComponentModel.DataAnnotations;

using AnalyticDashboard.Application.Auth.Accounts;
using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AnalyticDashboard.Application.Profiling;
using AnalyticDashboard.Infrastructure.Auth.Email;
using AnalyticDashboard.Infrastructure.Identity;
using AnalyticDashboard.Infrastructure.Services.Profiling;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Infrastructure.Storage;
using AnalyticDashboard.Application.Analytics.QueryDataset;
using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Application.Datasets.GetDatasetRows;
using AnalyticDashboard.Application.Widgets.Persistence;
using AnalyticDashboard.Infrastructure.Services.Analytics;
using AnalyticDashboard.Infrastructure.Services.DuckDb;
using AnalyticDashboard.Infrastructure.Services.Datasets;
using MimeKit;

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

        services.AddScoped<IUnitOfWork>(
            serviceProvider =>
                serviceProvider.GetRequiredService<AppDbContext>()
        );

        services.AddScoped<IDatasetRepository, DatasetRepository>();

        services.AddScoped<IDatasetVersionRepository, DatasetVersionRepository>();

        services.AddScoped<IImportJobRepository, ImportJobRepository>();

        services.AddScoped<IPendingDatasetImportRepository, PendingDatasetImportRepository>();

        services.AddScoped<IFileStorage, LocalFileStorage>();

        services.AddScoped<IDatasetProfileReader, DuckDbDatasetProfileReader>();

        services.AddScoped<DuckDbParquetExecutor>();

        services.AddScoped<IDatasetRowsReader, DuckDbDatasetRowsReader>();

        services.AddScoped<IDashboardRepository, DashboardRepository>();

        services.AddScoped<IWidgetRepository, WidgetRepository>();

        services.AddScoped<IProjectRepository, ProjectRepository>();

        services.AddScoped<IUserAccountService, UserAccountService>();

        services.AddScoped<IUserAccountTokenService, UserAccountTokenService>();

        services.AddSingleton<IEmailConfirmationLinkBuilder, EmailConfirmationLinkBuilder>();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection("Smtp"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host)
                && Uri.CheckHostName(options.Host) != UriHostNameType.Unknown,
                "Smtp:Host must be a host name or IP address.")
            .Validate(options => options.Port is >= 1 and <= 65535,
                "Smtp:Port must be between 1 and 65535.")
            .Validate(options => Enum.IsDefined(options.EncryptionMode),
                "Smtp:EncryptionMode must be None, StartTls or SslOnConnect.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FromEmail)
                && new EmailAddressAttribute().IsValid(options.FromEmail)
                && MailboxAddress.TryParse(options.FromEmail, out var address)
                && address.Address == options.FromEmail,
                "Smtp:FromEmail must be a valid email address.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FromName),
                "Smtp:FromName is required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.Username)
                == string.IsNullOrWhiteSpace(options.Password),
                "SMTP username and password must be provided together.")
            .Validate(options => options.EncryptionMode != SmtpEncryptionMode.None
                || string.IsNullOrWhiteSpace(options.Username),
                "SMTP authentication requires TLS encryption.")
            .Validate<IHostEnvironment>((options, environment) =>
                options.EncryptionMode != SmtpEncryptionMode.None
                || (environment.IsDevelopment() && options.AllowUnencryptedInDevelopment),
                "Unencrypted SMTP is allowed only when explicitly enabled in Development.")
            .Validate(_ => configuration["Smtp:UseSsl"] is null,
                "Smtp:UseSsl is no longer supported. Configure Smtp:EncryptionMode instead.")
            .ValidateOnStart();

        services.AddScoped<DuckDbCsvImporter>();

        services.AddScoped<ImportJobProcessor>();

        services.AddScoped<ImportJobRecoveryService>();

        services.AddScoped<IDatasetQueryReader, DuckDbDatasetQueryReader>();

        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddScoped<IStorageCleanupQueue, StorageCleanupQueue>();

        services.AddScoped<IDatasetDeletionStore, DatasetDeletionStore>();

        services.AddScoped<IProjectWriteTransactionFactory, ProjectWriteTransactionFactory>();

        services.AddScoped<StorageCleanupProcessor>();

        services.AddScoped<IProjectDeletionStore, ProjectDeletionStore>();

        var cleanupWorkerEnabled = configuration.GetValue(
            "StorageCleanup:WorkerEnabled",
            true
        );

        if (cleanupWorkerEnabled)
        {
            services.AddHostedService<StorageCleanupWorker>();
        }

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
