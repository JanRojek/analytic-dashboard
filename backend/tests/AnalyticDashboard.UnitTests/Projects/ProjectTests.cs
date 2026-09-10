using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.UnitTests.Projects;

public sealed class ProjectTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Constructor_ShouldTrimProjectName()
    {
        const string projectName = "  My Project  ";

        var project = new Project(UserId, projectName);

        Assert.Equal("My Project", project.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenNameIsNullOrWhitespace(
        string? name)
    {
        var exception = Assert.Throws<InvalidEntityNameException>(
            () => new Project(UserId, name!)
        );

        Assert.Equal(
            nameof(Project),
            exception.EntityType
        );
    }

    [Theory]
    [InlineData("a")]
    [InlineData("🍆")]
    public void Constructor_ShouldThrow_WhenNameExceedsMaxLength(
        string character)
    {
        var projectName = string.Concat(
            Enumerable.Repeat(
                character,
                Project.MaxNameLength + 1
            )
        );

        var exception = Assert.Throws<InvalidEntityNameException>(
            () => new Project(UserId, projectName)
        );

        Assert.Equal(
            nameof(Project),
            exception.EntityType
        );
    }

    [Theory]
    [InlineData("a")]
    [InlineData("🍆")]
    public void Constructor_ShouldAllowName_WhenNameHasExactlyMaxLength(
        string character)
    {
        var projectName = string.Concat(
            Enumerable.Repeat(
                character,
                Project.MaxNameLength
            )
        );

        var project = new Project(
            UserId,
            projectName
        );

        Assert.Equal(
            projectName,
            project.Name
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOwnerIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => new Project(Guid.Empty, "My Project")
        );
    }

    [Fact]
    public void Constructor_ShouldSetOwnerId()
    {
        var ownerId = Guid.NewGuid();

        var project = new Project(
            ownerId,
            "My Project"
        );

        Assert.Equal(
            ownerId,
            project.OwnerId
        );
    }

    [Fact]
    public void Constructor_ShouldGenerateUniqueNonEmptyId()
    {
        var firstProject = new Project(
            UserId,
            "First Project"
        );

        var secondProject = new Project(
            UserId,
            "Second Project"
        );

        Assert.NotEqual(
            Guid.Empty,
            firstProject.Id
        );

        Assert.NotEqual(
            Guid.Empty,
            secondProject.Id
        );

        Assert.NotEqual(
            firstProject.Id,
            secondProject.Id
        );
    }

    [Fact]
    public void Constructor_ShouldSetCreatedAtUtc()
    {
        var before = DateTime.UtcNow;

        var project = new Project(
            UserId,
            "My Project"
        );

        var after = DateTime.UtcNow;

        Assert.InRange(
            project.CreatedAtUtc,
            before,
            after
        );

        Assert.Equal(
            DateTimeKind.Utc,
            project.CreatedAtUtc.Kind
        );
    }

    [Fact]
    public void Rename_ShouldUpdateAndTrimProjectName()
    {
        const string projectName = "Old Name";

        var project = new Project(
            UserId,
            projectName
        );

        project.Rename("  New Name  ");

        Assert.Equal(
            "New Name",
            project.Name
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ShouldThrowAndKeepPreviousName_WhenNameIsNullOrWhitespace(
        string? name)
    {
        const string projectName = "Old Name";

        var project = new Project(
            UserId,
            projectName
        );

        var exception = Assert.Throws<InvalidEntityNameException>(
            () => project.Rename(name!)
        );

        Assert.Equal(
            nameof(Project),
            exception.EntityType
        );

        Assert.Equal(
            projectName,
            project.Name
        );
    }

    [Theory]
    [InlineData("a")]
    [InlineData("🍆")]
    public void Rename_ShouldThrowAndKeepPreviousName_WhenNameExceedsMaxLength(
        string character)
    {
        const string projectName = "Old Name";

        var project = new Project(
            UserId,
            projectName
        );

        var newName = string.Concat(
            Enumerable.Repeat(
                character,
                Project.MaxNameLength + 1
            )
        );

        var exception = Assert.Throws<InvalidEntityNameException>(
            () => project.Rename(newName)
        );

        Assert.Equal(
            nameof(Project),
            exception.EntityType
        );

        Assert.Equal(
            projectName,
            project.Name
        );
    }
}
