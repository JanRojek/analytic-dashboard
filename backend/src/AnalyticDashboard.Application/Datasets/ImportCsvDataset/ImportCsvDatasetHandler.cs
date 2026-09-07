using AnalyticDashboard.Application.Datasets.Persistence;
using AnalyticDashboard.Application.Import;
using AnalyticDashboard.Application.Projects.Persistence;
using AnalyticDashboard.Domain.Entities;

namespace AnalyticDashboard.Application.Datasets.ImportCsvDataset;

public sealed class ImportCsvDatasetHandler
{
    private readonly IDatasetRepository _datasetRepository;
    private readonly IDatasetVersionRepository _versionRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ICsvImportService _csvImportService;

    public ImportCsvDatasetHandler(
        IDatasetRepository datasetRepository,
        IDatasetVersionRepository versionRepository,
        IProjectRepository projectRepository,
        ICsvImportService csvImportService)
    {
        _datasetRepository = datasetRepository;
        _versionRepository = versionRepository;
        _projectRepository = projectRepository;
        _csvImportService = csvImportService;
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

        CsvImportResult importResult;

        try
        {
            importResult = await _csvImportService.ImportAsync(
                command.FileStream,
                command.FileName,
                cancellationToken
            );
        }
        catch (ArgumentException exception)
        {
            return new ImportCsvDatasetResult.InvalidFile(
                exception.Message
            );
        }
        catch (InvalidOperationException exception)
        {
            return new ImportCsvDatasetResult.InvalidFile(
                exception.Message
            );
        }

        var dataset = new Dataset(
            command.ProjectId,
            Path.GetFileNameWithoutExtension(
                importResult.OriginalFileName
            )
        );

        await _datasetRepository.AddAsync(
            dataset,
            cancellationToken
        );

        var version = new DatasetVersion(
            dataset.Id,
            versionNumber: 1,
            importResult.OriginalFileName,
            importResult.StoredPath
        );

        version.MarkReady(
            importResult.RowCount,
            importResult.ColumnCount
        );

        await _versionRepository.AddAsync(
            version,
            cancellationToken
        );

        var published =
            await _datasetRepository.PublishVersionAsync(
                dataset.Id,
                command.ProjectId,
                command.OwnerId,
                version.Id,
                cancellationToken
            );

        if (!published)
        {
            throw new InvalidOperationException(
                "The imported dataset version could not be published."
            );
        }

        return new ImportCsvDatasetResult.Success(
            dataset.Id
        );
    }
}
