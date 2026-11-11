namespace AnalyticDashboard.Application.Common.Persistence;

public interface IProjectWriteTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
