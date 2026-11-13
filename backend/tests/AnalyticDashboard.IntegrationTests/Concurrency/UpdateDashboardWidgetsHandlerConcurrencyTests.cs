using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Application.Dashboards.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class UpdateDashboardWidgetsHandlerConcurrencyTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateDashboardWidgetsHandlerConcurrencyTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_ShouldHandleRealConcurrencyException_AndRequeryRevisionBeforeReturning(
        bool deleteDashboard)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var cancellationToken = timeout.Token;
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Handler concurrency project");
        var dashboard = new Dashboard(project.Id, "Contended dashboard");
        var otherDashboard = new Dashboard(project.Id, "Unaffected dashboard");
        var dataset = new Dataset(project.Id, "Dataset");
        var updatedWidget = CreateWidget(dashboard.Id, dataset.Id, "Original update", 0);
        var deletedWidget = CreateWidget(dashboard.Id, dataset.Id, "Original delete", 12);
        var unaffectedWidget = CreateWidget(otherDashboard.Id, dataset.Id, "Unaffected", 0);

        await using var seedScope = _fixture.Services.CreateAsyncScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        seedContext.AddRange(
            project,
            dashboard,
            otherDashboard,
            dataset,
            updatedWidget,
            deletedWidget,
            unaffectedWidget
        );
        await seedContext.SaveChangesAsync(cancellationToken);

        var checkpoint = new SaveCheckpoint();
        await using var baseFactory = new CustomWebApplicationFactory(
            seedContext.Database.GetConnectionString()!
        );
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IDashboardRepository>(provider => new RecordingDashboardRepository(
                    new DashboardRepository(provider.GetRequiredService<AppDbContext>()),
                    checkpoint
                ));
                services.AddScoped<IUnitOfWork>(provider => new CoordinatedUnitOfWork(
                    provider.GetRequiredService<AppDbContext>(),
                    checkpoint
                ));
            })
        );
        using var client = factory.CreateClient();
        var newWidgetId = Guid.NewGuid();
        var updates = new List<DashboardWidgetUpdateRequest?>
        {
            new DashboardWidgetUpdateRequest(
                updatedWidget.Id,
                dataset.Id,
                WidgetType.BarChart,
                "Losing update must roll back",
                "Country",
                "Amount",
                "Sum",
                new WidgetLayoutRequest(0, 30, 12, 20)
            )
        };

        if (!deleteDashboard)
        {
            updates.Add(new DashboardWidgetUpdateRequest(
                newWidgetId,
                dataset.Id,
                WidgetType.BarChart,
                "Losing insert must roll back",
                "Country",
                "Amount",
                "Sum",
                new WidgetLayoutRequest(12, 30, 12, 20)
            ));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{project.Id}/dashboards/{dashboard.Id}/widgets"
        );
        request.Content = JsonContent.Create(new UpdateDashboardWidgetsRequest(updates, 0));
        request.Headers.Add(TestAuthHandler.UserIdHeader, ownerId.ToString());

        var sending = client.SendAsync(request, cancellationToken);
        Dictionary<Guid, WidgetSnapshot> expectedWidgets;
        try
        {
            await checkpoint.BeforeSave.Task.WaitAsync(cancellationToken);
            Assert.Equal(0L, checkpoint.LoadedRevision);
            Assert.Equal(0L, checkpoint.OriginalRevision);
            Assert.Equal(1L, checkpoint.StagedRevision);
            Assert.True(checkpoint.HasTransaction);
            Assert.Equal(EntityState.Modified, checkpoint.WidgetStates[updatedWidget.Id]);
            Assert.Equal(EntityState.Deleted, checkpoint.WidgetStates[deletedWidget.Id]);

            if (!deleteDashboard)
            {
                Assert.Equal(EntityState.Added, checkpoint.WidgetStates[newWidgetId]);
            }

            Assert.Null(checkpoint.ConcurrencyException);
            Assert.DoesNotContain("revision-requery", checkpoint.Calls);

            await using var competingScope = _fixture.Services.CreateAsyncScope();
            var competingContext = competingScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEqual(checkpoint.ContextId, competingContext.ContextId.InstanceId);
            var competingDashboard = await competingContext.Dashboards.SingleAsync(
                item => item.Id == dashboard.Id,
                cancellationToken
            );
            Assert.Equal(checkpoint.LoadedRevision, competingDashboard.Revision);

            // This writer deliberately bypasses the project lock to exercise the optimistic-concurrency fallback.
            if (deleteDashboard)
            {
                competingContext.Dashboards.Remove(competingDashboard);
                await competingContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var winningWidget = await competingContext.Widgets.SingleAsync(
                    item => item.Id == updatedWidget.Id,
                    cancellationToken
                );
                winningWidget.Rename("Winning update");
                winningWidget.UpdateLayout(new WidgetLayout(0, 60, 12, 20));
                competingDashboard.AdvanceRevision();
                await competingContext.SaveChangesAsync(cancellationToken);
                competingDashboard.AdvanceRevision();
                await competingContext.SaveChangesAsync(cancellationToken);
            }

            expectedWidgets = await ReadWidgetsAsync(
                competingContext,
                project.Id,
                cancellationToken
            );
        }
        finally
        {
            checkpoint.AllowSave.TrySetResult();
        }

        using var response = await sending.WaitAsync(cancellationToken);

        var concurrencyException = Assert.IsType<OptimisticConcurrencyException>(
            checkpoint.ConcurrencyException
        );
        var databaseException = Assert.IsType<DbUpdateConcurrencyException>(
            concurrencyException.InnerException
        );
        Assert.NotEmpty(databaseException.Entries);

        if (!deleteDashboard)
        {
            Assert.Contains(databaseException.Entries, entry =>
                entry.Entity is Dashboard item && item.Id == dashboard.Id);
        }

        Assert.Equal(
            new[] { "dashboard-read", "save", "concurrency-exception", "revision-requery" },
            checkpoint.Calls
        );
        Assert.Equal(
            deleteDashboard ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
            response.StatusCode
        );
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (deleteDashboard)
        {
            Assert.Null(checkpoint.RequeriedRevision);
            Assert.Equal(
                "Dashboard not found",
                problem.RootElement.GetProperty("title").GetString()
            );
        }
        else
        {
            Assert.Equal(2L, checkpoint.RequeriedRevision);
            Assert.Equal(0L, problem.RootElement.GetProperty("expectedRevision").GetInt64());
            Assert.Equal(2L, problem.RootElement.GetProperty("currentRevision").GetInt64());
        }

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actualWidgets = await ReadWidgetsAsync(
            verificationContext,
            project.Id,
            cancellationToken
        );

        AssertWidgetSnapshotsEqual(expectedWidgets, actualWidgets);
        Assert.False(await verificationContext.Widgets.AnyAsync(
            item => item.Id == newWidgetId,
            cancellationToken
        ));
        var finalRevision = await verificationContext.Dashboards
            .Where(item => item.Id == dashboard.Id)
            .Select(item => (long?)item.Revision)
            .SingleOrDefaultAsync(cancellationToken);
        Assert.Equal(deleteDashboard ? null : 2L, finalRevision);
        Assert.Equal(0L, await verificationContext.Dashboards
            .Where(item => item.Id == otherDashboard.Id)
            .Select(item => item.Revision)
            .SingleAsync(cancellationToken));
        Assert.True(await verificationContext.Datasets.AnyAsync(
            item => item.Id == dataset.Id,
            cancellationToken
        ));

        if (deleteDashboard)
        {
            Assert.Single(actualWidgets);
            Assert.False(actualWidgets.ContainsKey(updatedWidget.Id));
            Assert.False(actualWidgets.ContainsKey(deletedWidget.Id));
        }
        else
        {
            Assert.Equal(3, actualWidgets.Count);
            Assert.Equal("Winning update", actualWidgets[updatedWidget.Id].Title);
            Assert.Equal("Original delete", actualWidgets[deletedWidget.Id].Title);
        }
    }

    private static Widget CreateWidget(
        Guid dashboardId,
        Guid datasetId,
        string title,
        int x)
    {
        return new Widget(
            dashboardId,
            datasetId,
            WidgetType.BarChart,
            title,
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(x, 0, 12, 20)
        );
    }

    private static async Task<Dictionary<Guid, WidgetSnapshot>> ReadWidgetsAsync(
        AppDbContext context,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var widgets = await context.Widgets
            .AsNoTracking()
            .Where(widget => context.Dashboards.Any(dashboard =>
                dashboard.Id == widget.DashboardId && dashboard.ProjectId == projectId
            ))
            .ToListAsync(cancellationToken);

        return widgets.ToDictionary(
            widget => widget.Id,
            widget => new WidgetSnapshot(
                widget.Id,
                widget.DashboardId,
                widget.DatasetId,
                widget.Type,
                widget.Title,
                widget.GroupByColumn,
                widget.MeasureColumn,
                widget.Aggregation,
                widget.Layout,
                widget.CreatedAtUtc
            )
        );
    }

    private static void AssertWidgetSnapshotsEqual(
        IReadOnlyDictionary<Guid, WidgetSnapshot> expected,
        IReadOnlyDictionary<Guid, WidgetSnapshot> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());

        foreach (var (widgetId, expectedWidget) in expected)
        {
            var actualWidget = actual[widgetId];

            Assert.Equal(expectedWidget.Id, actualWidget.Id);
            Assert.Equal(expectedWidget.DashboardId, actualWidget.DashboardId);
            Assert.Equal(expectedWidget.DatasetId, actualWidget.DatasetId);
            Assert.Equal(expectedWidget.Type, actualWidget.Type);
            Assert.Equal(expectedWidget.Title, actualWidget.Title);
            Assert.Equal(expectedWidget.GroupByColumn, actualWidget.GroupByColumn);
            Assert.Equal(expectedWidget.MeasureColumn, actualWidget.MeasureColumn);
            Assert.Equal(expectedWidget.Aggregation, actualWidget.Aggregation);
            Assert.Equal(expectedWidget.Layout, actualWidget.Layout);
            Assert.Equal(expectedWidget.CreatedAtUtc, actualWidget.CreatedAtUtc);
        }
    }

    private sealed record WidgetSnapshot(
        Guid Id,
        Guid DashboardId,
        Guid? DatasetId,
        WidgetType Type,
        string Title,
        string GroupByColumn,
        string MeasureColumn,
        AggregationType Aggregation,
        WidgetLayout Layout,
        DateTime CreatedAtUtc
    );

    private sealed class SaveCheckpoint
    {
        public TaskCompletionSource BeforeSave { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource AllowSave { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public List<string> Calls { get; } = [];

        public long? LoadedRevision { get; set; }
        public long OriginalRevision { get; set; }
        public long StagedRevision { get; set; }
        public long? RequeriedRevision { get; set; }

        public Guid ContextId { get; set; }
        public bool HasTransaction { get; set; }
        public Dictionary<Guid, EntityState> WidgetStates { get; set; } = [];
        public OptimisticConcurrencyException? ConcurrencyException { get; set; }
    }

    private sealed class CoordinatedUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;
        private readonly SaveCheckpoint _checkpoint;

        public CoordinatedUnitOfWork(
            AppDbContext context,
            SaveCheckpoint checkpoint)
        {
            _context = context;
            _checkpoint = checkpoint;
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            var dashboard = _context.ChangeTracker.Entries<Dashboard>().Single();
            _checkpoint.OriginalRevision = dashboard.Property(item => item.Revision).OriginalValue;
            _checkpoint.StagedRevision = dashboard.Entity.Revision;
            _checkpoint.ContextId = _context.ContextId.InstanceId;
            _checkpoint.HasTransaction = _context.Database.CurrentTransaction is not null;
            _checkpoint.WidgetStates = _context.ChangeTracker.Entries<Widget>()
                .ToDictionary(entry => entry.Entity.Id, entry => entry.State);

            _checkpoint.Calls.Add("save");
            _checkpoint.BeforeSave.SetResult();
            await _checkpoint.AllowSave.Task.WaitAsync(cancellationToken);

            try
            {
                return await ((IUnitOfWork)_context).SaveChangesAsync(cancellationToken);
            }
            catch (OptimisticConcurrencyException exception)
            {
                _checkpoint.ConcurrencyException = exception;
                _checkpoint.Calls.Add("concurrency-exception");
                throw;
            }
        }
    }

    private sealed class RecordingDashboardRepository : IDashboardRepository
    {
        private readonly IDashboardRepository _inner;
        private readonly SaveCheckpoint _checkpoint;

        public RecordingDashboardRepository(
            IDashboardRepository inner,
            SaveCheckpoint checkpoint)
        {
            _inner = inner;
            _checkpoint = checkpoint;
        }

        public Task AddAsync(
            Dashboard dashboard,
            CancellationToken cancellationToken) =>
            _inner.AddAsync(dashboard, cancellationToken);

        public Task<IReadOnlyList<Dashboard>> GetAllByProjectAndOwnerAsync(
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.GetAllByProjectAndOwnerAsync(projectId, ownerId, cancellationToken);

        public async Task<Dashboard?> GetForUpdateByIdAndProjectOwnerAsync(
            Guid dashboardId,
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            var dashboard = await _inner.GetForUpdateByIdAndProjectOwnerAsync(
                dashboardId,
                projectId,
                ownerId,
                cancellationToken
            );
            _checkpoint.LoadedRevision = dashboard?.Revision;
            _checkpoint.Calls.Add("dashboard-read");

            return dashboard;
        }

        public Task<Dashboard?> GetByIdAndProjectOwnerAsync(
            Guid dashboardId,
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.GetByIdAndProjectOwnerAsync(dashboardId, projectId, ownerId, cancellationToken);

        public Task<bool> DeleteByIdAndProjectOwnerAsync(
            Guid dashboardId,
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.DeleteByIdAndProjectOwnerAsync(dashboardId, projectId, ownerId, cancellationToken);

        public async Task<long?> GetDashboardCurrentRevisionAsync(
            Guid dashboardId,
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            _checkpoint.Calls.Add("revision-requery");
            var revision = await _inner.GetDashboardCurrentRevisionAsync(
                dashboardId,
                projectId,
                ownerId,
                cancellationToken
            );
            _checkpoint.RequeriedRevision = revision;

            return revision;
        }
    }
}
