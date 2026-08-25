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
    public void Constructor_ShouldThrow_WhenNameIsNullOrWhitespace(string? name)
    {
        Assert.Throws<InvalidProjectNameException>(
            () => new Project(UserId, name!)
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsTooLong()
    {
        var projectName = new string(
            'a',
            Project.MaxNameLength + 1
        );

        Assert.Throws<InvalidProjectNameException>(
            () => new Project(UserId, projectName)
        );
    }

    [Fact]
    public void Constructor_ShouldAllowName_WhenNameHasMaxLength()
    {
        var projectName = new string(
            'a',
            Project.MaxNameLength
        );

        var project = new Project(UserId, projectName);

        Assert.Equal(projectName, project.Name);
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

        var project = new Project(ownerId, "My Project");

        Assert.Equal(ownerId, project.OwnerId);
    }

    [Fact]
    public void Constructor_ShouldGenerateNonEmptyId()
    {
        var project = new Project(UserId, "My Project");

        Assert.NotEqual(Guid.Empty, project.Id);
    }

    [Fact]
    public void Constructor_ShouldSetCreatedAtUtc()
    {
        var project = new Project(UserId, "My Project");

        Assert.NotEqual(default, project.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, project.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Rename_ShouldUpdateAndTrimProjectName()
    {
        const string projectName = "Old Name";

        var project = new Project(UserId, projectName);

        project.Rename("  New Name  ");

        Assert.Equal("New Name", project.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ShouldThrowAndKeepPreviousName_WhenNameIsNullOrWhitespace(string? name)
    {
        const string projectName = "Old Name";

        var project = new Project(UserId, projectName);

        Assert.Throws<InvalidProjectNameException>(
            () => project.Rename(name!)
        );

        Assert.Equal(projectName, project.Name);
    }

    [Fact]
    public void Rename_ShouldThrowAndKeepPreviousName_WhenNameIsTooLong()
    {
        const string projectName = "Old Name";

        var project = new Project(UserId, projectName);

        var newName = new string(
            'a',
            Project.MaxNameLength + 1
        );

        Assert.Throws<InvalidProjectNameException>(
            () => project.Rename(newName)
        );

        Assert.Equal(projectName, project.Name);
    }

    [Fact]
    public void Constructor_ShouldAllowName_WhenNameHasExactlyMaxUnicodeCharacters()
    {
        var name = string.Concat(
            Enumerable.Repeat(
                "🍆",
                Project.MaxNameLength
            )
        );

        var project = new Project(
            Guid.NewGuid(),
            name
        );

        Assert.Equal(
            name,
            project.Name
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameExceedsMaxUnicodeCharacters()
    {
        var name = string.Concat(
            Enumerable.Repeat(
                "🍆",
                Project.MaxNameLength + 1
            )
        );

        Assert.Throws<InvalidProjectNameException>(
            () => new Project(
                Guid.NewGuid(),
                name
            )
        );
    }
}
