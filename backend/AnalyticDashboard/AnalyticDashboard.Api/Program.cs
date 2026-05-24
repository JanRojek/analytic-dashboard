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
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AnalyticDashboard.Application.Dashboards.DeleteDashboard;
using AnalyticDashboard.Application.Dashboards.GetDashboardById;
using AnalyticDashboard.Application.Dashboards.GetDashboards;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

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

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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

builder.Services.AddScoped<JwtTokenService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/db");

app.MapDatasetEndpoints();
app.MapAuthEndpoints();
app.MapDashboardEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();