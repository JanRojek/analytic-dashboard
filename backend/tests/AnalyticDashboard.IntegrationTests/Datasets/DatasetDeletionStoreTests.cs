using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class DatasetDeletionStoreTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DatasetDeletionStoreTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteAsync_ShouldDetachWidgetsAndEnqueueCleanup_WhenDatasetCanBeDeleted()
    {
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Deletion test project");
        var dashboard = new Dashboard(project.Id, "Dashboard");
        var dataset = new Dataset(project.Id, "Dataset");
        var version = new DatasetVersion(dataset.Id, 1, "source.csv");

        var firstWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            WidgetType.BarChart,
            "First widget",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            dashboard.Id,
            dataset.Id,
            WidgetType.BarChart,
            "Second widget",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(12, 0, 12, 20)
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.AddRange(
                project,
                dashboard,
                dataset,
                version,
                firstWidget,
                secondWidget
            );

            await context.SaveChangesAsync(CancellationToken);
        }

        DatasetDeletionOutcome result;

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var deletionStore = scope.ServiceProvider
                .GetRequiredService<IDatasetDeletionStore>();

            result = await deletionStore.DeleteAsync(
                dataset.Id,
                project.Id,
                ownerId,
                CancellationToken
            );
        }

        Assert.Equal(DatasetDeletionOutcome.Deleted, result);

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.False(await verificationContext.Datasets
            .AnyAsync(item => item.Id == dataset.Id, CancellationToken));

        Assert.False(await verificationContext.DatasetVersions
            .AnyAsync(item => item.DatasetId == dataset.Id, CancellationToken));

        var widgets = await verificationContext.Widgets
            .AsNoTracking()
            .Where(widget => widget.DashboardId == dashboard.Id)
            .ToListAsync(CancellationToken);

        Assert.Equal(2, widgets.Count);
        Assert.All(widgets, widget => Assert.Null(widget.DatasetId));

        Assert.Contains(widgets, widget =>
            widget.Id == firstWidget.Id
            && widget.Title == "First widget"
            && widget.GroupByColumn == "Country"
            && widget.MeasureColumn == "Amount");

        Assert.Contains(widgets, widget =>
            widget.Id == secondWidget.Id
            && widget.Title == "Second widget");

        var revision = await verificationContext.Dashboards
            .Where(item => item.Id == dashboard.Id)
            .Select(item => item.Revision)
            .SingleAsync(CancellationToken);

        Assert.Equal(1L, revision);

        var cleanupJob = await verificationContext.StorageCleanupJobs
            .SingleAsync(job => job.DatasetId == dataset.Id, CancellationToken);

        Assert.Equal(0, cleanupJob.AttemptCount);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRejectDataset_WhenImportIsPending()
    {
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Pending import project");
        var dataset = new Dataset(project.Id, "Importing dataset");
        var version = new DatasetVersion(dataset.Id, 1, "source.csv");

        var importJob = new ImportJob(
            version.Id,
            "source.csv",
            $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.AddRange(project, dataset, version, importJob);
            await context.SaveChangesAsync(CancellationToken);
        }

        DatasetDeletionOutcome result;

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var deletionStore = scope.ServiceProvider
                .GetRequiredService<IDatasetDeletionStore>();

            result = await deletionStore.DeleteAsync(
                dataset.Id,
                project.Id,
                ownerId,
                CancellationToken
            );
        }

        Assert.Equal(DatasetDeletionOutcome.ImportInProgress, result);

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.True(await verificationContext.Datasets
            .AnyAsync(item => item.Id == dataset.Id, CancellationToken));

        Assert.True(await verificationContext.ImportJobs
            .AnyAsync(job => job.Id == importJob.Id, CancellationToken));

        Assert.False(await verificationContext.StorageCleanupJobs
            .AnyAsync(job => job.DatasetId == dataset.Id, CancellationToken));
    }
}
