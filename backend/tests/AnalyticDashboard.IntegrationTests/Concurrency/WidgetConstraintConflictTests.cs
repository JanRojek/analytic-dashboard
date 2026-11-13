using System.Net;
using System.Net.Http.Json;

using AnalyticDashboard.Api.Contracts.Widgets;
using AnalyticDashboard.Application.Common.Exceptions;
using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Domain.Analytics;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Domain.ValueObjects;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class WidgetConstraintConflictTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public WidgetConstraintConflictTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_ShouldRejectGlobalIdConflict_WithoutDisclosingOtherWidgetOrChangingState(
        bool foreignOwner)
    {
        var seed = await SeedAsync(foreignOwner, existingCollision: true);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await ReadStateAsync(context, seed);
        var checkpoint = new SaveCheckpoint(pause: false);
        await using var baseFactory = new CustomWebApplicationFactory(context.Database.GetConnectionString()!);
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IUnitOfWork>(provider => new ObservedUnitOfWork(
                    provider.GetRequiredService<AppDbContext>(),
                    checkpoint
                ))
            )
        );
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{seed.ProjectId}/dashboards/{seed.DashboardId}/widgets"
        );
        request.Content = JsonContent.Create(CreateBody(seed));
        request.Headers.Add(TestAuthHandler.UserIdHeader, seed.OwnerId.ToString());

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.IsType<WidgetIdConflictException>(checkpoint.Failure);
        AssertPostgresFailure(
            checkpoint.Failure,
            PostgresErrorCodes.UniqueViolation,
            "PK_Widgets"
        );
        await AssertSafeBadRequestAsync(response, "Invalid widget set.");
        var payload = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain(seed.CollisionId.ToString(), payload);
        Assert.DoesNotContain(seed.OtherDashboardId.ToString(), payload);
        Assert.DoesNotContain("Private widget", payload);
        var after = await ReadStateAsync(context, seed);

        AssertStateEqual(before, after);
        Assert.Equal(0L, after.Revisions[seed.DashboardId]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Put_ShouldRollbackAllChanges_WhenIdIsClaimedOrDatasetIsDeletedBeforeSave(
        bool deleteDataset)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var seed = await SeedAsync(foreignOwner: true, existingCollision: false);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkpoint = new SaveCheckpoint(pause: true);
        await using var baseFactory = new CustomWebApplicationFactory(context.Database.GetConnectionString()!);
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IUnitOfWork>(provider => new ObservedUnitOfWork(
                    provider.GetRequiredService<AppDbContext>(),
                    checkpoint
                ))
            )
        );
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/projects/{seed.ProjectId}/dashboards/{seed.DashboardId}/widgets"
        );
        request.Content = JsonContent.Create(CreateBody(seed));
        request.Headers.Add(TestAuthHandler.UserIdHeader, seed.OwnerId.ToString());

        var sending = client.SendAsync(request, cancellationToken);
        SavedState expected;
        try
        {
            await checkpoint.BeforeSave.Task.WaitAsync(cancellationToken);
            Assert.Equal(0L, checkpoint.OriginalRevision);
            Assert.Equal(1L, checkpoint.StagedRevision);
            Assert.True(checkpoint.HasTransaction);
            Assert.Equal(EntityState.Modified, checkpoint.WidgetStates[seed.UpdatedWidgetId]);
            Assert.Equal(EntityState.Deleted, checkpoint.WidgetStates[seed.DeletedWidgetId]);
            Assert.Equal(EntityState.Added, checkpoint.WidgetStates[seed.FreshWidgetId]);
            Assert.Equal(EntityState.Added, checkpoint.WidgetStates[seed.CollisionId]);
            await using var competingScope = _fixture.Services.CreateAsyncScope();
            var competing = competingScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEqual(checkpoint.ContextId, competing.ContextId.InstanceId);
            Assert.False(await competing.Widgets.AnyAsync(
                item => item.Id == seed.CollisionId,
                cancellationToken
            ));

            if (deleteDataset)
            {
                // Bypass the AUD-001 project lock to verify defensive FK handling.
                var dataset = await competing.Datasets.SingleAsync(
                    item => item.Id == seed.ReferencedDatasetId,
                    cancellationToken
                );
                Assert.False(await competing.Widgets.AnyAsync(
                    item => item.DatasetId == dataset.Id,
                    cancellationToken
                ));
                competing.Datasets.Remove(dataset);
                await competing.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var transactions = competingScope.ServiceProvider
                    .GetRequiredService<IProjectWriteTransactionFactory>();
                await using var transaction = await transactions.BeginAsync(
                    seed.OtherProjectId,
                    cancellationToken
                );
                Assert.NotNull(transaction);
                var dashboard = await competing.Dashboards.SingleAsync(
                    item => item.Id == seed.OtherDashboardId,
                    cancellationToken
                );
                competing.Widgets.Add(CreateWidget(
                    seed.OtherDashboardId,
                    seed.OtherDatasetId,
                    seed.CollisionId,
                    "Private widget"
                ));
                dashboard.AdvanceRevision();
                await competing.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            expected = await ReadStateAsync(competing, seed);
        }
        finally
        {
            checkpoint.AllowSave.TrySetResult();
        }
        using var response = await sending.WaitAsync(cancellationToken);

        if (deleteDataset)
        {
            var failure = Assert.IsType<MissingReferenceException>(checkpoint.Failure);
            Assert.Equal(nameof(Dataset), failure.EntityType);
            AssertPostgresFailure(
                failure,
                PostgresErrorCodes.ForeignKeyViolation,
                "FK_Widgets_datasets_DatasetId"
            );
            await AssertSafeBadRequestAsync(response, "Dataset not found");
        }
        else
        {
            Assert.IsType<WidgetIdConflictException>(checkpoint.Failure);
            AssertPostgresFailure(
                checkpoint.Failure,
                PostgresErrorCodes.UniqueViolation,
                "PK_Widgets"
            );
            await AssertSafeBadRequestAsync(response, "Invalid widget set.");
        }

        var after = await ReadStateAsync(context, seed);

        AssertStateEqual(expected, after);
        Assert.Equal(0L, after.Revisions[seed.DashboardId]);
        Assert.False(await context.Widgets.AnyAsync(
            item => item.Id == seed.FreshWidgetId,
            cancellationToken
        ));
        Assert.Equal(!deleteDataset, await context.Datasets.AnyAsync(
            item => item.Id == seed.ReferencedDatasetId,
            cancellationToken
        ));
    }

    [Theory]
    [InlineData("Length", PostgresErrorCodes.StringDataRightTruncation)]
    [InlineData("OtherUnique", PostgresErrorCodes.UniqueViolation)]
    [InlineData("OtherForeignKey", PostgresErrorCodes.ForeignKeyViolation)]
    public async Task UnitOfWork_ShouldPreserveUnexpectedDatabaseErrors(string scenario, string expectedCode)
    {
        var seed = await SeedAsync(foreignOwner: false, existingCollision: false);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var widget = CreateWidget(seed.DashboardId, seed.StableDatasetId, Guid.NewGuid(), "Valid title");
        if (scenario == "Length")
        {
            context.Widgets.Add(widget);
            context.Entry(widget).Property(item => item.Title).CurrentValue =
                new string('a', Widget.MaxTitleLength + 1);
        }
        else if (scenario == "OtherUnique")
        {
            context.Projects.Add(new Project(seed.OwnerId, "Target project"));
        }
        else
        {
            context.Dashboards.Add(new Dashboard(Guid.NewGuid(), "Missing project"));
        }

        var unitOfWork = (IUnitOfWork)context;
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken)
        );

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(expectedCode, postgresException.SqlState);

        await using var verificationScope = _fixture.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verification.Widgets.AnyAsync(
            item => item.Id == widget.Id,
            CancellationToken
        ));
    }

    [Fact]
    public async Task UnitOfWork_ShouldTranslateOnlyKnownWidgetDashboardForeignKey()
    {
        var seed = await SeedAsync(foreignOwner: false, existingCollision: false);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Widgets.Add(CreateWidget(
            Guid.NewGuid(),
            seed.StableDatasetId,
            Guid.NewGuid(),
            "Missing dashboard"
        ));

        var unitOfWork = (IUnitOfWork)context;
        var exception = await Assert.ThrowsAsync<MissingReferenceException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken)
        );

        Assert.Equal(nameof(Dashboard), exception.EntityType);
        AssertPostgresFailure(
            exception,
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_Widgets_Dashboards_DashboardId"
        );
    }

    private async Task<Seed> SeedAsync(bool foreignOwner, bool existingCollision)
    {
        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Target project");
        var otherProject = foreignOwner ? new Project(Guid.NewGuid(), "Private project") : project;
        var dashboard = new Dashboard(project.Id, "Target dashboard");
        var otherDashboard = new Dashboard(otherProject.Id, "Private dashboard");
        var stable = new Dataset(project.Id, "Stable dataset");
        var referenced = new Dataset(project.Id, "Referenced dataset");
        var otherDataset = new Dataset(otherProject.Id, "Private dataset");
        var updated = CreateWidget(dashboard.Id, stable.Id, Guid.NewGuid(), "Original update");
        var deleted = CreateWidget(dashboard.Id, stable.Id, Guid.NewGuid(), "Original delete");
        deleted.UpdateLayout(new WidgetLayout(12, 0, 12, 20));
        var collisionId = Guid.NewGuid();
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.AddRange(project, dashboard, otherDashboard, stable, referenced, otherDataset, updated, deleted);
        if (foreignOwner)
        {
            context.Projects.Add(otherProject);
        }
        if (existingCollision)
        {
            context.Widgets.Add(CreateWidget(otherDashboard.Id, otherDataset.Id, collisionId, "Private widget"));
        }
        await context.SaveChangesAsync(CancellationToken);

        return new Seed(
            ownerId,
            project.Id,
            otherProject.Id,
            dashboard.Id,
            otherDashboard.Id,
            stable.Id,
            referenced.Id,
            otherDataset.Id,
            updated.Id,
            deleted.Id,
            Guid.NewGuid(),
            collisionId
        );
    }

    private static Widget CreateWidget(
        Guid dashboardId,
        Guid datasetId,
        Guid id,
        string title)
    {
        return new Widget(
            dashboardId,
            datasetId,
            WidgetType.BarChart,
            title,
            "Country",
            "Amount",
            AggregationType.Sum,
            new WidgetLayout(0, 0, 12, 20),
            id
        );
    }

    private static UpdateDashboardWidgetsRequest CreateBody(Seed seed)
    {
        return new UpdateDashboardWidgetsRequest(
            [
                new DashboardWidgetUpdateRequest(
                    seed.UpdatedWidgetId,
                    seed.StableDatasetId,
                    WidgetType.BarChart,
                    "Must roll back update",
                    "Country",
                    "Amount",
                    "Sum",
                    new WidgetLayoutRequest(0, 30, 8, 20)
                ),
                new DashboardWidgetUpdateRequest(
                    seed.FreshWidgetId,
                    seed.ReferencedDatasetId,
                    WidgetType.BarChart,
                    "Must roll back insert",
                    "Country",
                    "Amount",
                    "Sum",
                    new WidgetLayoutRequest(8, 30, 8, 20)
                ),
                new DashboardWidgetUpdateRequest(
                    seed.CollisionId,
                    seed.ReferencedDatasetId,
                    WidgetType.BarChart,
                    "Must roll back conflicting insert",
                    "Country",
                    "Amount",
                    "Sum",
                    new WidgetLayoutRequest(16, 30, 8, 20)
                )
            ],
            0
        );
    }

    private static async Task<SavedState> ReadStateAsync(
        AppDbContext context,
        Seed seed)
    {
        Guid[] dashboardIds = [seed.DashboardId, seed.OtherDashboardId];
        var widgets = await context.Widgets
            .AsNoTracking()
            .Where(item => dashboardIds.Contains(item.DashboardId))
            .ToListAsync(CancellationToken);
        var revisions = await context.Dashboards
            .AsNoTracking()
            .Where(item => dashboardIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Revision, CancellationToken);
        return new SavedState(
            widgets.ToDictionary(item => item.Id, item => new WidgetSnapshot(
                item.Id,
                item.DashboardId,
                item.DatasetId,
                item.Type,
                item.Title,
                item.GroupByColumn,
                item.MeasureColumn,
                item.Aggregation,
                item.Layout,
                item.CreatedAtUtc
            )),
            revisions
        );
    }

    private static void AssertStateEqual(SavedState expected, SavedState actual)
    {
        Assert.Equal(
            expected.Widgets.Keys.OrderBy(id => id),
            actual.Widgets.Keys.OrderBy(id => id)
        );

        foreach (var (id, expectedWidget) in expected.Widgets)
        {
            var actualWidget = actual.Widgets[id];

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

        Assert.Equal(
            expected.Revisions.OrderBy(item => item.Key),
            actual.Revisions.OrderBy(item => item.Key)
        );
    }

    private static void AssertPostgresFailure(Exception? failure, string code, string constraint)
    {
        Assert.NotNull(failure);
        var databaseException = Assert.IsType<DbUpdateException>(failure.InnerException);
        var postgresException = Assert.IsType<PostgresException>(
            databaseException.InnerException
        );
        Assert.Equal(code, postgresException.SqlState);
        Assert.Equal("Widgets", postgresException.TableName);
        Assert.Equal(constraint, postgresException.ConstraintName);
    }

    private static async Task AssertSafeBadRequestAsync(HttpResponseMessage response, string title)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(title, problem.Title);
        var payload = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("PK_Widgets", payload);
        Assert.DoesNotContain("FK_Widgets", payload);
        Assert.DoesNotContain("PostgresException", payload);
    }

    private sealed record Seed(
        Guid OwnerId,
        Guid ProjectId,
        Guid OtherProjectId,
        Guid DashboardId,
        Guid OtherDashboardId,
        Guid StableDatasetId,
        Guid ReferencedDatasetId,
        Guid OtherDatasetId,
        Guid UpdatedWidgetId,
        Guid DeletedWidgetId,
        Guid FreshWidgetId,
        Guid CollisionId
    );

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

    private sealed record SavedState(
        Dictionary<Guid, WidgetSnapshot> Widgets,
        Dictionary<Guid, long> Revisions
    );

    private sealed class SaveCheckpoint
    {
        public TaskCompletionSource BeforeSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; set; }
        public long OriginalRevision { get; set; }
        public long StagedRevision { get; set; }
        public Guid ContextId { get; set; }
        public bool HasTransaction { get; set; }
        public Dictionary<Guid, EntityState> WidgetStates { get; set; } = [];

        public SaveCheckpoint(bool pause)
        {
            if (!pause)
            {
                AllowSave.SetResult();
            }
        }
    }

    private sealed class ObservedUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;
        private readonly SaveCheckpoint _checkpoint;

        public ObservedUnitOfWork(
            AppDbContext context,
            SaveCheckpoint checkpoint)
        {
            _context = context;
            _checkpoint = checkpoint;
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            var dashboard = _context.ChangeTracker.Entries<Dashboard>().Single();
            _checkpoint.OriginalRevision = dashboard
                .Property(item => item.Revision)
                .OriginalValue;
            _checkpoint.StagedRevision = dashboard.Entity.Revision;
            _checkpoint.ContextId = _context.ContextId.InstanceId;
            _checkpoint.HasTransaction = _context.Database.CurrentTransaction is not null;
            _checkpoint.WidgetStates = _context.ChangeTracker.Entries<Widget>()
                .ToDictionary(entry => entry.Entity.Id, entry => entry.State);
            _checkpoint.BeforeSave.SetResult();
            await _checkpoint.AllowSave.Task.WaitAsync(cancellationToken);
            try
            {
                return await ((IUnitOfWork)_context).SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _checkpoint.Failure = exception;
                throw;
            }
        }
    }
}
