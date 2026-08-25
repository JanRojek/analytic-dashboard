using AnalyticDashboard.Domain.Entities;
using AnalyticDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Projects;

public sealed class ProjectDatabaseConstraintsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static CancellationToken CancellationToken =>
        TestContext.Current.CancellationToken;

    public ProjectDatabaseConstraintsTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(
        ProjectConstraintScenario.EmptyOwnerId,
        "CK_projects_OwnerId_NotEmpty"
    )]
    [InlineData(
        ProjectConstraintScenario.EmptyName,
        "CK_projects_Name_NotBlank"
    )]
    [InlineData(
        ProjectConstraintScenario.NameTooLong,
        "CK_projects_Name_MaxLength"
    )]
    [InlineData(
        ProjectConstraintScenario.UntrimmedName,
        "CK_projects_Name_Trimmed"
    )]
    public async Task ProjectDatabase_ShouldRejectInvalidData_WhenCheckConstraintIsViolated(
        ProjectConstraintScenario scenario,
        string expectedConstraintName)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                CancellationToken
            );

        var validOwnerId = Guid.NewGuid();

        var validName =
            $"Database constraint {Guid.NewGuid():N}";

        var (ownerId, name) = scenario switch
        {
            ProjectConstraintScenario.EmptyOwnerId =>
                (
                    Guid.Empty,
                    validName
                ),

            ProjectConstraintScenario.EmptyName =>
                (
                    validOwnerId,
                    string.Empty
                ),

            ProjectConstraintScenario.NameTooLong =>
                (
                    validOwnerId,
                    new string(
                        'A',
                        Project.MaxNameLength + 1
                    )
                ),

            ProjectConstraintScenario.UntrimmedName =>
                (
                    validOwnerId,
                    " Untrimmed project "
                ),

            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                null
            )
        };

        var projectId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () =>
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO projects (
                        "Id",
                        "OwnerId",
                        "Name",
                        "CreatedAtUtc"
                    )
                    VALUES (
                        {projectId},
                        {ownerId},
                        {name},
                        {createdAtUtc}
                    )
                    """,
                    CancellationToken
                )
        );

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState
        );

        Assert.Equal(
            expectedConstraintName,
            exception.ConstraintName
        );
    }

    [Theory]
    [InlineData("\t\t")]
    [InlineData("\n")]
    [InlineData("\u00A0")]
    [InlineData("\tProject\n")]
    [InlineData("\u00A0Project\u00A0")]
    public async Task ProjectDatabase_ShouldRejectName_WhenWhitespaceIsNotNormalized(
        string name)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                CancellationToken
            );

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () =>
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO projects (
                         "Id",
                         "OwnerId",
                         "Name",
                         "CreatedAtUtc"
                     )
                     VALUES (
                         {Guid.NewGuid()},
                         {Guid.NewGuid()},
                         {name},
                         {DateTime.UtcNow}
                     )
                     """,
                    CancellationToken
                )
        );

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState
        );
    }

    public enum ProjectConstraintScenario
    {
        EmptyOwnerId,
        EmptyName,
        NameTooLong,
        UntrimmedName
    }
}
