using System.Diagnostics;
using AnalyticDashboard.Application.Datasets.Persistence;

namespace AnalyticDashboard.Application.Datasets.DeleteDataset;

public sealed class DeleteDatasetHandler
{
    private readonly IDatasetDeletionStore _deletionStore;

    public DeleteDatasetHandler(IDatasetDeletionStore deletionStore)
    {
        _deletionStore = deletionStore;
    }

    public async Task<DeleteDatasetResult> HandleAsync(
        DeleteDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await _deletionStore.DeleteAsync(
            command.DatasetId,
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        return outcome switch
        {
            DatasetDeletionOutcome.Deleted =>
                new DeleteDatasetResult.Success(),

            DatasetDeletionOutcome.NotFound =>
                new DeleteDatasetResult.NotFound(),

            DatasetDeletionOutcome.ImportInProgress =>
                new DeleteDatasetResult.ImportInProgress(),

            _ => throw new UnreachableException()
        };
    }
}
