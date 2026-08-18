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

        var result = new Project(UserId, projectName);

        Assert.Equal("My Project", result.Name);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        Assert.Throws<InvalidProjectNameException>(
            () => new Project(UserId, "")
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsTooLong()
    {
        var projectName = new string('a', 101);

        Assert.Throws<InvalidProjectNameException>(
            () => new Project(UserId, projectName)
        );
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOwnerIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Project(Guid.Empty, "My Project"));
    }

    [Fact]
    public void Rename_ShouldUpdateAndTrimProjectName()
    {
        const string projectName = "Old Name";

        var result = new Project(UserId, projectName);

        result.Rename("  New Name  ");

        Assert.Equal("New Name", result.Name);
    }
}
