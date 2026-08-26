using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class ProjectRepositoryTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ProjectRepositoryTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_ShouldLeaveContextUsable_AfterNameConflict()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider
            .GetRequiredService<IProjectRepository>();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var userId = Guid.NewGuid();

        var existingProject = new Project(
            userId,
            "Existing project"
        );

        var existingOutcome = await repository.AddAsync(
            existingProject,
            CancellationToken
        );

        Assert.Equal(
            ProjectAddOutcome.Added,
            existingOutcome
        );

        var duplicateProject = new Project(
            userId,
            "existing project"
        );

        var duplicateOutcome = await repository.AddAsync(
            duplicateProject,
            CancellationToken
        );

        Assert.Equal(
            ProjectAddOutcome.NameAlreadyExists,
            duplicateOutcome
        );

        Assert.Equal(
            EntityState.Detached,
            dbContext.Entry(duplicateProject).State
        );

        var validProject = new Project(
            userId,
            "Valid project after conflict"
        );

        var validOutcome = await repository.AddAsync(
            validProject,
            CancellationToken
        );

        Assert.Equal(
            ProjectAddOutcome.Added,
            validOutcome
        );

        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == userId)
            .ToListAsync(CancellationToken);

        Assert.Equal(
            2,
            projects.Count
        );

        Assert.Contains(
            projects,
            project => project.Id == existingProject.Id
        );

        Assert.Contains(
            projects,
            project => project.Id == validProject.Id
        );

        Assert.DoesNotContain(
            projects,
            project => project.Id == duplicateProject.Id
        );
    }
}
