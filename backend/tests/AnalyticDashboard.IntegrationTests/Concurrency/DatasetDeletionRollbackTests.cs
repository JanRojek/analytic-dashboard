using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class DatasetDeletionRollbackTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DatasetDeletionRollbackTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteDataset_ShouldRollbackAllChanges_WhenDatabaseDeletionFails()
    {
        var cancellationToken = CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Rollback test project");
        var dataset = new Dataset(project.Id, "Rollback test dataset");

        var firstDashboard = new Dashboard(project.Id, "First dashboard");
        var secondDashboard = new Dashboard(project.Id, "Second dashboard");

        var firstWidget = new Widget(
            firstDashboard.Id,
            dataset.Id,
            WidgetType.BarChart,
            "Revenue",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            secondDashboard.Id,
            dataset.Id,
            WidgetType.PieChart,
            "Orders",
            "Category",
            "Amount",
            AggregationType.Count,
            new WidgetLayout(0, 0, 12, 20)
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(
                project,
                dataset,
                firstDashboard,
                secondDashboard,
                firstWidget,
                secondWidget
            );

            await context.SaveChangesAsync(cancellationToken);
        }

        Dictionary<Guid, Widget> beforeWidgets;
        Dictionary<Guid, long> beforeRevisions;

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            beforeWidgets = await context.Widgets
                .AsNoTracking()
                .Where(widget =>
                    widget.DashboardId == firstDashboard.Id
                    || widget.DashboardId == secondDashboard.Id
                )
                .ToDictionaryAsync(
                    widget => widget.Id,
                    cancellationToken
                );

            beforeRevisions = await context.Dashboards
                .AsNoTracking()
                .Where(dashboard => dashboard.ProjectId == project.Id)
                .ToDictionaryAsync(
                    dashboard => dashboard.Id,
                    dashboard => dashboard.Revision,
                    cancellationToken
                );
        }

        var failureId = Guid.NewGuid();

        try
        {
            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                await context.Database.ExecuteSqlRawAsync(
                    CreateFunctionSql(failureId), cancellationToken);

                await context.Database.ExecuteSqlRawAsync(
                    CreateTriggerSql(failureId, dataset.Id), cancellationToken);
            }

            await using (var scope = _fixture.Services.CreateAsyncScope())
            {
                var deletionStore = scope.ServiceProvider
                    .GetRequiredService<IDatasetDeletionStore>();

                var exception = await Record.ExceptionAsync(
                    () => deletionStore.DeleteAsync(
                        dataset.Id,
                        project.Id,
                        ownerId,
                        cancellationToken
                    )
                );

                Assert.NotNull(exception);

                var postgresException = Assert.IsType<PostgresException>(
                    exception.GetBaseException()
                );

                Assert.Equal("P0001", postgresException.SqlState);
            }

            await using var verificationScope =
                _fixture.Services.CreateAsyncScope();

            var verificationContext = verificationScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var datasetExists = await verificationContext.Datasets
                .AsNoTracking()
                .AnyAsync(
                    item => item.Id == dataset.Id,
                    cancellationToken
                );

            Assert.True(datasetExists);

            var afterWidgets = await verificationContext.Widgets
                .AsNoTracking()
                .Where(widget =>
                    widget.DashboardId == firstDashboard.Id
                    || widget.DashboardId == secondDashboard.Id
                )
                .ToDictionaryAsync(
                    widget => widget.Id,
                    cancellationToken
                );

            Assert.Equal(beforeWidgets.Count, afterWidgets.Count);

            foreach (var (widgetId, original) in beforeWidgets)
            {
                Assert.True(afterWidgets.TryGetValue(widgetId, out var saved));

                Assert.NotNull(saved);

                Assert.Equal(original.DashboardId, saved.DashboardId);
                Assert.Equal(original.DatasetId, saved.DatasetId);
                Assert.Equal(original.Type, saved.Type);
                Assert.Equal(original.Title, saved.Title);
                Assert.Equal(original.GroupByColumn, saved.GroupByColumn);
                Assert.Equal(original.MeasureColumn, saved.MeasureColumn);
                Assert.Equal(original.Aggregation, saved.Aggregation);
                Assert.Equal(original.Layout, saved.Layout);
                Assert.Equal(original.CreatedAtUtc, saved.CreatedAtUtc);
            }

            var afterRevisions = await verificationContext.Dashboards
                .AsNoTracking()
                .Where(dashboard => dashboard.ProjectId == project.Id)
                .ToDictionaryAsync(
                    dashboard => dashboard.Id,
                    dashboard => dashboard.Revision,
                    cancellationToken
                );

            Assert.Equal(beforeRevisions.Count, afterRevisions.Count);

            foreach (var (dashboardId, revision) in beforeRevisions)
            {
                Assert.Equal(revision, afterRevisions[dashboardId]);
            }

            var cleanupJobExists = await verificationContext.StorageCleanupJobs
                .AsNoTracking()
                .AnyAsync(
                    job => job.DatasetId == dataset.Id,
                    cancellationToken
                );

            Assert.False(cleanupJobExists);
        }
        finally
        {
            await using var scope = _fixture.Services.CreateAsyncScope();

            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            await context.Database.ExecuteSqlRawAsync(
                DropTriggerSql(failureId), CancellationToken.None);

            await context.Database.ExecuteSqlRawAsync(
                DropFunctionSql(failureId), CancellationToken.None);
        }
    }

    private static string FunctionName(Guid id) => $"test_fail_delete_{id:N}";

    private static string CreateFunctionSql(Guid id) =>
        $"""
         CREATE FUNCTION "{FunctionName(id)}"()
         RETURNS trigger
         LANGUAGE plpgsql
         AS $$
         BEGIN
             RAISE EXCEPTION 'Forced dataset deletion failure'
                 USING ERRCODE = 'P0001';

             RETURN OLD;
         END;
         $$;
         """;

    private static string CreateTriggerSql(Guid id, Guid datasetId) =>
        $"""
         CREATE TRIGGER "{FunctionName(id)}"
         AFTER DELETE ON datasets
         FOR EACH ROW
         WHEN (OLD."Id" = '{datasetId}'::uuid)
         EXECUTE FUNCTION "{FunctionName(id)}"();
         """;

    private static string DropTriggerSql(Guid id) =>
        $"""DROP TRIGGER IF EXISTS "{FunctionName(id)}" ON datasets;""";

    private static string DropFunctionSql(Guid id) =>
        $"""DROP FUNCTION IF EXISTS "{FunctionName(id)}"();""";
}
