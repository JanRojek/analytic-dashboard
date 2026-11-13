using AnalyticDashboard.Application.Dashboards.CreateDashboard;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using AnalyticDashboard.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class CreateDashboardReferenceConflictTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateDashboardReferenceConflictTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_ShouldReturnProjectNotFound_WhenProjectIsDeletedAfterValidation()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;

        var ownerId = Guid.NewGuid();
        var project = new Project(ownerId, "Deleted during dashboard creation");
        await using var seedScope = _fixture.Services.CreateAsyncScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        seedContext.Projects.Add(project);
        await seedContext.SaveChangesAsync(cancellationToken);

        var barrier = new SaveBarrier();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(seedContext.Database.GetConnectionString())
            .AddInterceptors(barrier)
            .Options;
        await using var context = new AppDbContext(options);
        var handler = new CreateDashboardHandler(
            new DashboardRepository(context),
            new ProjectRepository(context)
        );

        var creating = handler.HandleAsync(
            new CreateDashboardCommand(project.Id, ownerId, "Must not be persisted"),
            cancellationToken
        );
        try
        {
            await barrier.BeforeSave.Task.WaitAsync(cancellationToken);
            var staged = Assert.Single(context.ChangeTracker.Entries<Dashboard>());
            Assert.Equal(EntityState.Added, staged.State);
            Assert.Equal(project.Id, staged.Entity.ProjectId);
            await using var competingScope = _fixture.Services.CreateAsyncScope();
            var competing = competingScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEqual(context.ContextId.InstanceId, competing.ContextId.InstanceId);
            var deletedProject = await competing.Projects.SingleAsync(
                item => item.Id == project.Id,
                cancellationToken
            );
            competing.Projects.Remove(deletedProject);
            await competing.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            barrier.AllowSave.TrySetResult();
        }

        var result = await creating.WaitAsync(cancellationToken);

        Assert.IsType<CreateDashboardResult.ProjectNotFound>(result);
        var updateException = Assert.IsType<DbUpdateException>(barrier.Failure);
        var postgresException = Assert.IsType<PostgresException>(
            updateException.InnerException
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgresException.SqlState);
        Assert.Equal("Dashboards", postgresException.TableName);
        Assert.Equal("FK_Dashboards_projects_ProjectId", postgresException.ConstraintName);
        Assert.False(await seedContext.Dashboards.AnyAsync(
            item => item.ProjectId == project.Id,
            cancellationToken
        ));
        Assert.False(await seedContext.Projects.AnyAsync(
            item => item.Id == project.Id,
            cancellationToken
        ));
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        public TaskCompletionSource BeforeSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            BeforeSave.SetResult();
            await AllowSave.Task.WaitAsync(cancellationToken);
            return result;
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Failure = eventData.Exception;
            return Task.CompletedTask;
        }
    }
}
