using AnalyticDashboard.Application.Common.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

public sealed class ProjectWriteTransactionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ProjectWriteTransactionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BeginAsync_ShouldBlockConcurrentProjectWrites_UntilTransactionEnds()
    {
        var project = new Project(Guid.NewGuid(), "Concurrency test");

        await using (var seedScope = _fixture.Services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            context.Projects.Add(project);
            await context.SaveChangesAsync(CancellationToken);
        }

        await using var firstScope = _fixture.Services.CreateAsyncScope();
        await using var secondScope = _fixture.Services.CreateAsyncScope();

        var transactionFactory = firstScope.ServiceProvider
            .GetRequiredService<IProjectWriteTransactionFactory>();

        var secondContext = secondScope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await using (var transaction = await transactionFactory.BeginAsync(
            project.Id,
            CancellationToken
        ))
        {
            Assert.NotNull(transaction);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => secondContext.Projects
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM projects
                        WHERE "Id" = {project.Id}
                        FOR NO KEY UPDATE NOWAIT
                        """)
                    .AsNoTracking()
                    .ToListAsync(CancellationToken)
            );

            var postgresException = Assert.IsType<PostgresException>(
                exception.InnerException
            );

            Assert.Equal(
                PostgresErrorCodes.LockNotAvailable,
                postgresException.SqlState
            );
        }

        var projects = await secondContext.Projects
            .FromSqlInterpolated($"""
                SELECT *
                FROM projects
                WHERE "Id" = {project.Id}
                FOR NO KEY UPDATE NOWAIT
                """)
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        Assert.Single(projects);
    }
}
