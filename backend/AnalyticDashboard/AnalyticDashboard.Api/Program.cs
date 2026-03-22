using AnalyticDashboard.Api.Endpoints;
using AnalyticDashboard.Infrastructure;
using AnalyticDashboard.Application.Datasets.GetDatasetById;
using AnalyticDashboard.Application.Datasets.GetDatasets;
using AnalyticDashboard.Application.Datasets.DeleteDataset;
using AnalyticDashboard.Application.Datasets.ImportCsvDataset;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);

builder.Services.AddScoped<GetDatasetsHandler>();
builder.Services.AddScoped<GetDatasetByIdHandler>();
builder.Services.AddScoped<DeleteDatasetHandler>();
builder.Services.AddScoped<ImportCsvDatasetHandler>();

var app = builder.Build();

app.MapHealthChecks("/health/db");

app.MapDatasetEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();