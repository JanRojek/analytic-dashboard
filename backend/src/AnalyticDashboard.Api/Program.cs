using AnalyticDashboard.Api.Endpoints;
using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;
using AnalyticDashboard.Application.Datasets.GetDatasetProfile;
using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;
using AnalyticDashboard.Application.Widgets.CreateWidget;
using AnalyticDashboard.Application.Widgets.DeleteWidget;
using AnalyticDashboard.Application.Widgets.GetWidgets;
using System.Text.Json.Serialization;
using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Application.Auth.CompleteRegistration;
using AnalyticDashboard.Application.Auth.ConfirmEmail;
using AnalyticDashboard.Application.Auth.CurrentUser;
using AnalyticDashboard.Application.Auth.Email;
using AnalyticDashboard.Application.Auth.ForgotPassword;
using AnalyticDashboard.Application.Auth.Login;
using AnalyticDashboard.Application.Auth.Logout;
using AnalyticDashboard.Application.Auth.Register;
using AnalyticDashboard.Application.Auth.RegistrationStatus;
using AnalyticDashboard.Application.Auth.ResendConfirmation;
using AnalyticDashboard.Application.Auth.ResetPassword;
using AnalyticDashboard.Application.Projects.CreateProject;
using AnalyticDashboard.Application.Projects.DeleteProject;
using AnalyticDashboard.Application.Projects.GetProjectById;
using AnalyticDashboard.Application.Projects.GetProjects;
using AnalyticDashboard.Application.Projects.RenameProject;
using AnalyticDashboard.Infrastructure.Auth.Email;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter()
    );
});

builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();

builder.Services.AddInfrastructure(
    builder.Configuration
);

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Stores.SchemaVersion =
            IdentitySchemaVersions.Version2;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);

builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = false;
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme =
            IdentityConstants.ApplicationScheme;

        options.DefaultSignInScheme =
            IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    };
});

builder.Services.AddScoped<GetDatasetsHandler>();
builder.Services.AddScoped<GetDatasetByIdHandler>();
builder.Services.AddScoped<DeleteDatasetHandler>();
builder.Services.AddScoped<ImportCsvDatasetHandler>();
builder.Services.AddScoped<GetDatasetProfileHandler>();
builder.Services.AddScoped<CreateDashboardHandler>();
builder.Services.AddScoped<GetDashboardsHandler>();
builder.Services.AddScoped<GetDashboardByIdHandler>();
builder.Services.AddScoped<DeleteDashboardHandler>();
builder.Services.AddScoped<CreateWidgetHandler>();
builder.Services.AddScoped<GetWidgetsHandler>();
builder.Services.AddScoped<DeleteWidgetHandler>();
builder.Services.AddScoped<CreateProjectHandler>();
builder.Services.AddScoped<GetProjectByIdHandler>();
builder.Services.AddScoped<GetProjectsHandler>();
builder.Services.AddScoped<RenameProjectHandler>();
builder.Services.AddScoped<DeleteProjectHandler>();
builder.Services.AddScoped<RegisterUserHandler>();
builder.Services.AddScoped<ConfirmEmailHandler>();
builder.Services.AddScoped<GetRegistrationStatusHandler>();
builder.Services.AddScoped<CompleteRegistrationHandler>();
builder.Services.AddScoped<LoginUserHandler>();
builder.Services.AddScoped<LogoutUserHandler>();
builder.Services.AddScoped<GetCurrentUserHandler>();
builder.Services.AddScoped<ResendConfirmationHandler>();
builder.Services.AddScoped<ForgotPasswordHandler>();
builder.Services.AddScoped<ResetPasswordHandler>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IRegistrationSessionService, RegistrationSessionService>();
builder.Services.AddSingleton<IPasswordResetLinkBuilder, PasswordResetLinkBuilder>();

builder.Services.AddScoped<EmailConfirmationSender>();
builder.Services.AddScoped<PasswordResetEmailSender>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/db");

app.MapDatasetEndpoints();
app.MapDashboardEndpoints();
app.MapProjectEndpoints();
app.MapAuthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
