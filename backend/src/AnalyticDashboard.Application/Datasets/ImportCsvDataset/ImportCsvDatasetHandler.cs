using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Application.Storage;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public sealed class ImportCsvDatasetHandler
{
    private readonly IProjectRepository _projectRepository;
    private readonly IPendingDatasetImportRepository _pendingImportRepository;
    private readonly IFileStorage _fileStorage;

    public ImportCsvDatasetHandler(
        IProjectRepository projectRepository,
        IPendingDatasetImportRepository pendingImportRepository,
        IFileStorage fileStorage)
    {
        _projectRepository = projectRepository;
        _pendingImportRepository = pendingImportRepository;
        _fileStorage = fileStorage;
    }

    public async Task<ImportCsvDatasetResult> HandleAsync(
        ImportCsvDatasetCommand command,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAndOwnerAsync(
            command.ProjectId,
            command.OwnerId,
            cancellationToken
        );

        if (project is null)
        {
            return new ImportCsvDatasetResult.ProjectNotFound();
        }

        var originalFileName = Path.GetFileName(
            command.FileName
        );

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return new ImportCsvDatasetResult.InvalidFile(
                "File name cannot be empty."
            );
        }

        var extension = Path.GetExtension(
            originalFileName
        );

        if (!string.Equals(
                extension,
                ".csv",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ImportCsvDatasetResult.InvalidFile(
                "File is not a .csv file."
            );
        }

        var datasetName = Path.GetFileNameWithoutExtension(
            originalFileName
        );

        if (string.IsNullOrWhiteSpace(datasetName))
        {
            return new ImportCsvDatasetResult.InvalidFile(
                "Dataset name cannot be empty."
            );
        }

        var dataset = new Dataset(
            command.ProjectId,
            datasetName
        );

        var version = new DatasetVersion(
            dataset.Id,
            versionNumber: 1,
            originalFileName
        );

        var sourceStorageKey =
            $"datasets/{dataset.Id:N}/imports/{version.Id:N}/source.csv";

        var importJob = new ImportJob(
            version.Id,
            originalFileName,
            sourceStorageKey
        );

        try
        {
            await _fileStorage.SaveAsync(
                sourceStorageKey,
                command.FileStream,
                cancellationToken
            );

            await _pendingImportRepository.CreateAsync(
                dataset,
                version,
                importJob,
                cancellationToken
            );
        }
        catch (Exception importException)
        {
            try
            {
                await _fileStorage.DeleteAsync(
                    sourceStorageKey,
                    CancellationToken.None
                );
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Dataset import creation failed and source cleanup also failed.",
                    importException,
                    cleanupException
                );
            }

            throw;
        }

        return new ImportCsvDatasetResult.Accepted(
            dataset.Id,
            version.Id,
            importJob.Id
        );
    }
}
