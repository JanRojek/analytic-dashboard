using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Datasets;

public sealed class DeleteDatasetEndpointTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DeleteDatasetEndpointTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static HttpRequestMessage CreateDeleteRequest(
        Guid projectId,
        Guid datasetId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{projectId}/datasets/{datasetId}"
        );

        if (userId.HasValue)
        {
            request.Headers.Add(
                TestAuthHandler.UserIdHeader,
                userId.Value.ToString()
            );
        }

        return request;
    }

    private async Task AddDatasetAsync(
        Project project,
        Dataset dataset)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        dbContext.Projects.Add(project);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );

        dbContext.Datasets.Add(dataset);

        await dbContext.SaveChangesAsync(
            CancellationToken
        );
    }

    [Fact]
    public async Task DeleteDataset_ShouldReturnNoContent_WhenDatasetBelongsToAuthenticatedUser()
    {
        var userId = Guid.NewGuid();

        var project = new Project(
            userId,
            "Delete dataset project"
        );

        var dataset = CreateDataset(
            project.Id,
            "Dataset to delete"
        );

        await AddDatasetAsync(
            project,
            dataset
        );

        using var request = CreateDeleteRequest(
            project.Id,
            dataset.Id,
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Datasets.AnyAsync(
            entity => entity.Id == dataset.Id,
            CancellationToken
        );

        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteDataset_ShouldReturnNotFound_WhenDatasetBelongsToDifferentUser()
    {
        var project = new Project(
            TestAuthHandler.User2Id,
            "User2 delete project"
        );

        var dataset = CreateDataset(
            project.Id,
            "Private dataset"
        );

        await AddDatasetAsync(
            project,
            dataset
        );

        using var request = CreateDeleteRequest(
            project.Id,
            dataset.Id,
            TestAuthHandler.User1Id
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            CancellationToken
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode
        );

        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await dbContext.Datasets.AnyAsync(
            entity => entity.Id == dataset.Id,
            CancellationToken
        );

        Assert.True(exists);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteDataset_ShouldReturnConflict_WhenImportIsActive(
        bool running)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Import conflict project");
        var dataset = new Dataset(project.Id, "Import dataset");

        var version = new DatasetVersion(
            dataset.Id,
            1,
            "source.csv"
        );

        var job = new ImportJob(
            version.Id,
            "source.csv",
            $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv"
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(project, dataset, version, job);

            if (running)
            {
                context.Entry(job).Property(item => item.Status).CurrentValue = ImportJobStatus.Running;
                context.Entry(job).Property(item => item.StartedAtUtc).CurrentValue = DateTime.UtcNow;
                context.Entry(job).Property(item => item.AttemptNumber).CurrentValue = 1;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{project.Id}/datasets/{dataset.Id}"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            ownerId.ToString()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            cancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken
        );

        Assert.Equal(
            "dataset_import_in_progress",
            problem.GetProperty("code").GetString()
        );

        await using var verificationScope =
            _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        Assert.True(await verificationContext.Datasets
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == dataset.Id,
                cancellationToken
            ));

        var savedJob = await verificationContext.ImportJobs
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == job.Id,
                cancellationToken
            );

        Assert.Equal(job.Status, savedJob.Status);

        Assert.False(await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.DatasetId == dataset.Id,
                cancellationToken
            ));
    }


    [Fact]
    public async Task DeleteDataset_ShouldDetachWidgetsAcrossDashboards_AndIncrementEachRevisionOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Dataset deletion project");

        var sourceDataset = new Dataset(project.Id, "Source dataset");
        var otherDataset = new Dataset(project.Id, "Other dataset");

        var firstDashboard = new Dashboard(project.Id, "First dashboard");
        var secondDashboard = new Dashboard(project.Id, "Second dashboard");
        var unaffectedDashboard = new Dashboard(project.Id, "Unaffected dashboard");

        var firstWidget = new Widget(
            firstDashboard.Id,
            sourceDataset.Id,
            WidgetType.BarChart,
            "Revenue",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20)
        );

        var secondWidget = new Widget(
            firstDashboard.Id,
            sourceDataset.Id,
            WidgetType.PieChart,
            "Orders",
            "Category",
            "Count",
            AggregationType.Count,
            new WidgetLayout(12, 0, 12, 20)
        );

        var thirdWidget = new Widget(
            secondDashboard.Id,
            sourceDataset.Id,
            WidgetType.BarChart,
            "Sales",
            "Region",
            "Value",
            AggregationType.Average,
            new WidgetLayout(0, 0, 12, 20)
        );

        var unaffectedWidget = new Widget(
            unaffectedDashboard.Id,
            otherDataset.Id,
            WidgetType.BarChart,
            "Other source",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20)
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(
                project,
                sourceDataset,
                otherDataset,
                firstDashboard,
                secondDashboard,
                unaffectedDashboard,
                firstWidget,
                secondWidget,
                thirdWidget,
                unaffectedWidget
            );

            await context.SaveChangesAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/projects/{project.Id}/datasets/{sourceDataset.Id}"
        );

        request.Headers.Add(
            TestAuthHandler.UserIdHeader,
            ownerId.ToString()
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            cancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope =
            _fixture.Services.CreateAsyncScope();

        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var datasets = await verificationContext.Datasets
            .AsNoTracking()
            .Where(dataset => dataset.ProjectId == project.Id)
            .Select(dataset => dataset.Id)
            .ToListAsync(cancellationToken);

        Assert.DoesNotContain(sourceDataset.Id, datasets);
        Assert.Contains(otherDataset.Id, datasets);

        var savedWidgets = await verificationContext.Widgets
            .AsNoTracking()
            .Where(widget =>
                widget.DashboardId == firstDashboard.Id
                || widget.DashboardId == secondDashboard.Id
                || widget.DashboardId == unaffectedDashboard.Id
            )
            .ToDictionaryAsync(widget => widget.Id, cancellationToken);

        Assert.Equal(4, savedWidgets.Count);

        foreach (var original in new[]
        {
            firstWidget,
            secondWidget,
            thirdWidget
        })
        {
            var saved = savedWidgets[original.Id];

            Assert.Null(saved.DatasetId);

            Assert.Equal(original.DashboardId, saved.DashboardId);
            Assert.Equal(original.Type, saved.Type);
            Assert.Equal(original.Title, saved.Title);
            Assert.Equal(original.GroupByColumn, saved.GroupByColumn);
            Assert.Equal(original.MeasureColumn, saved.MeasureColumn);
            Assert.Equal(original.Aggregation, saved.Aggregation);
            Assert.Equal(original.Layout, saved.Layout);
            Assert.Equal(
                original.CreatedAtUtc,
                saved.CreatedAtUtc,
                TimeSpan.FromMicroseconds(1)
            );
        }

        var preservedWidget = savedWidgets[unaffectedWidget.Id];

        Assert.Equal(otherDataset.Id, preservedWidget.DatasetId);
        Assert.Equal(unaffectedWidget.Title, preservedWidget.Title);
        Assert.Equal(unaffectedWidget.Layout, preservedWidget.Layout);

        var revisions = await verificationContext.Dashboards
            .AsNoTracking()
            .Where(dashboard => dashboard.ProjectId == project.Id)
            .ToDictionaryAsync(
                dashboard => dashboard.Id,
                dashboard => dashboard.Revision,
                cancellationToken
            );

        Assert.Equal(1L, revisions[firstDashboard.Id]);
        Assert.Equal(1L, revisions[secondDashboard.Id]);
        Assert.Equal(0L, revisions[unaffectedDashboard.Id]);

        var cleanupJobs = await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .Where(job => job.DatasetId == sourceDataset.Id)
            .ToListAsync(cancellationToken);

        Assert.Single(cleanupJobs);
    }

    private static Dataset CreateDataset(
        Guid projectId,
        string name)
    {
        return new Dataset(
            projectId,
            name
        );
    }
}
