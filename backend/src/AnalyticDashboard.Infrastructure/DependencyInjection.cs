using AnalyticDashboard.Application.Import;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Repositories;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;
using AnalyticDashboard.Infrastructure.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AnalyticDashboard.Application.Profiling;
using AnalyticDashboard.Infrastructure.Services.Profiling;
using AnalyticDashboard.Infrastructure.Services.Csv;

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

        services.AddScoped<ICsvImportService, CsvImportService>();

        services.AddScoped<IDatasetProfileReader, CsvDatasetProfileReader>();

        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<IDashboardRepository, DashboardRepository>();

        services.AddScoped<IWidgetRepository, WidgetRepository>();

        services.AddScoped<IProjectRepository, ProjectRepository>();

        services.AddScoped<CsvFormatDetector>();

        services.AddScoped<CsvDatasetReader>();

        return services;
    }
}
