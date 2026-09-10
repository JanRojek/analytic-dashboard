namespace AnalyticDashboard.Domain.Entities;

public sealed class Dashboard
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public const int MaxNameLength = 100;

    public Dashboard(
        Guid projectId,
        string name)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("ProjectId cannot be empty.");
        }

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Name = NormalizeName(name);
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        Name = NormalizeName(name);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidEntityNameException(
                nameof(Dashboard),
                "Dashboard name cannot be empty.");
        }

        name = name.Trim();

        if (name.EnumerateRunes().Count() > MaxNameLength)
        {
            throw new InvalidEntityNameException(
                nameof(Dashboard),
                $"Dashboard name cannot be longer than {MaxNameLength} characters.");
        }

        return name;
    }
}
