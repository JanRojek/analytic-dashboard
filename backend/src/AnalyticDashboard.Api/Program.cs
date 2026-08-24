using AnalyticDashboard.Api.Auth;
using AnalyticDashboard.Api.Endpoints;
using AnalyticDashboard.Application.Auth.Login;
using AnalyticDashboard.Application.Auth.Register;
using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;
using AnalyticDashboard.Application.Datasets.GetDatasetProfile;
using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;
using AnalyticDashboard.Application.Widgets.CreateWidget;
using AnalyticDashboard.Application.Widgets.DeleteWidget;
using AnalyticDashboard.Application.Widgets.GetWidgets;
using System.Text.Json.Serialization;
using AnalyticDashboard.Application.Projects.CreateProject;
using AnalyticDashboard.Application.Projects.DeleteProject;
using AnalyticDashboard.Application.Projects.GetProjectById;
using AnalyticDashboard.Application.Projects.GetProjects;
using AnalyticDashboard.Application.Projects.RenameProject;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste your JWT token here"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var jwtKey = builder.Configuration["Jwt:Key"];
var keyBytes = Encoding.UTF8.GetBytes(jwtKey!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddScoped<GetDatasetsHandler>();
builder.Services.AddScoped<GetDatasetByIdHandler>();
builder.Services.AddScoped<DeleteDatasetHandler>();
builder.Services.AddScoped<ImportCsvDatasetHandler>();
builder.Services.AddScoped<RegisterUserHandler>();
builder.Services.AddScoped<LoginUserHandler>();
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

builder.Services.AddScoped<JwtTokenService>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/db");

app.MapDatasetEndpoints();
app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapProjectEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
