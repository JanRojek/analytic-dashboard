namespace AnalyticDashboard.Application.Common.Persistence;

public interface IProjectWriteTransactionFactory
{
    Task<IProjectWriteTransaction?> BeginAsync(
        Guid projectId,
        CancellationToken cancellationToken
    );
}
