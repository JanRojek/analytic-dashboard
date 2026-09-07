namespace AnalyticDashboard.Domain.Entities;

public sealed class Dataset
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public string Name { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Dataset(
        Guid projectId,
        string name)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException(
                "ProjectId cannot be empty."
            );
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Dataset name cannot be empty."
            );
        }

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Name = name.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void PublishVersion(
        Guid versionId)
    {
        if (versionId == Guid.Empty)
        {
            throw new ArgumentException(
                "VersionId cannot be empty."
            );
        }

        CurrentVersionId = versionId;
    }
}
