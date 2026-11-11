using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class DatasetWidgetConcurrencyTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public DatasetWidgetConcurrencyTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentPutAndDelete_ShouldPreserveWidgetAndConsistentRevision()
    {
        var cancellationToken = CancellationToken;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Concurrency project");
        var dashboard = new Dashboard(project.Id, "Dashboard");
        var dataset = new Dataset(project.Id, "Dataset");

        var widget = new Widget(
            dashboard.Id,
            dataset.Id,
            WidgetType.BarChart,
            "Original title",
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20)
        );

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.AddRange(project, dashboard, dataset, widget);
            await context.SaveChangesAsync(cancellationToken);
        }

        var requestBody = new UpdateDashboardWidgetsRequest(
            [
                new DashboardWidgetUpdateRequest(
                    widget.Id,
                    dataset.Id,
                    widget.Type,
                    "Updated title",
                    widget.GroupByColumn,
                    widget.MeasureColumn,
                    widget.Aggregation.ToString(),
                    new WidgetLayoutRequest(0, 0, 12, 20)
                )
            ],
            0
        );

        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        async Task<DatasetDeletionOutcome> DeleteAsync()
        {
            await startGate.Task.WaitAsync(cancellationToken);

            await using var scope = _fixture.Services.CreateAsyncScope();

            var deletionStore = scope.ServiceProvider
                .GetRequiredService<IDatasetDeletionStore>();

            return await deletionStore.DeleteAsync(
                dataset.Id,
                project.Id,
                ownerId,
                cancellationToken
            );
        }

        async Task<HttpResponseMessage> PutAsync()
        {
            await startGate.Task.WaitAsync(cancellationToken);

            using var putRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"/projects/{project.Id}/dashboards/{dashboard.Id}/widgets");

            putRequest.Content = JsonContent.Create(requestBody);

            putRequest.Headers.Add(
                TestAuthHandler.UserIdHeader,
                ownerId.ToString()
            );

            return await _fixture.Client.SendAsync(putRequest, cancellationToken);
        }

        var deleteTask = DeleteAsync();
        var putTask = PutAsync();

        startGate.SetResult();

        await Task.WhenAll(deleteTask, putTask)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(
            DatasetDeletionOutcome.Deleted,
            await deleteTask
        );

        using var putResponse = await putTask;

        Assert.True(
            putResponse.StatusCode is
                HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected PUT status: {putResponse.StatusCode}"
        );

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

        var savedWidget = await verificationContext.Widgets
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == widget.Id,
                cancellationToken
            );

        var savedRevision = await verificationContext.Dashboards
            .AsNoTracking()
            .Where(item => item.Id == dashboard.Id)
            .Select(item => item.Revision)
            .SingleAsync(cancellationToken);

        var cleanupJobs = await verificationContext.StorageCleanupJobs
            .AsNoTracking()
            .Where(item => item.DatasetId == dataset.Id)
            .ToListAsync(cancellationToken);

        Assert.False(datasetExists);

        Assert.Equal(widget.Id, savedWidget.Id);
        Assert.Null(savedWidget.DatasetId);

        Assert.Single(cleanupJobs);

        if (putResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal("Updated title", savedWidget.Title);
            Assert.Equal(2L, savedRevision);
        }
        else
        {
            Assert.Equal("Original title", savedWidget.Title);
            Assert.Equal(1L, savedRevision);
        }
    }
}
