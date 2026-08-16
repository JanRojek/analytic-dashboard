namespace AnalyticDashboard.Domain.Entities;

public sealed class Project
{
    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public Project(
        Guid ownerId,
        string name)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("OwnerId cannot be empty.");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Name = NormalizeName(name);
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        Name = NormalizeName(name);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name cannot be empty.");
        }

        name = name.Trim();

        if (name.Length > 100)
        {
            throw new ArgumentException("Project name cannot be longer than 100 characters.");
        }

        return name;
    }
}
