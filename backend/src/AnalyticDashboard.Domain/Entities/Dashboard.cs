namespace AnalyticDashboard.Domain.Entities;

public sealed class Dashboard
{
    public Guid Id { get; private set; }

    public Guid DatasetId { get; private set; }
    
    public Dataset? Dataset { get; private set; }

    public string Name { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Dashboard(
        Guid id,
        Guid datasetId,
        string name,
        DateTime createdAtUtc)
    {
        Id = id;
        DatasetId = datasetId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }
}