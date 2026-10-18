import { useEffect, useState } from "react";
import { Alert, Button } from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import {
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";
import { queryKeys } from "../../../data/queryKeys";
import type { DatasetProfile, TransformStep } from "../../../data/types";
import { number, date } from "../formatters";
import { mockPreparation } from "../../../mocks/preparation";
import { useSession } from "../../../session/SessionContext";

import {
  useDataset,
  useDatasetProfile,
  useDatasetRows,
  useImportStatus,
} from "../hooks";

import { AppliedStepsPanel } from "./AppliedStepsPanel";
import { DatasetIssuesPanel } from "./DatasetIssuesPanel";
import { DataPreview } from "./DataPreview";

const missingCells = (profile: DatasetProfile) =>
  profile.columns.reduce((total, column) => total + column.nullCount, 0);

const completeness = (profile: DatasetProfile) =>
  profile.rowCount && profile.columnCount
    ? Math.max(
        0,
        100 -
          (missingCells(profile) / (profile.rowCount * profile.columnCount)) *
            100,
      )
    : 100;

export function DatasetWorkspace({
  projectId,
  datasetId,
}: {
  projectId: string;
  datasetId: string;
}) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const dataset = useDataset(projectId, datasetId);
  const importStatus = useImportStatus(
    projectId,
    datasetId,
    Boolean(dataset.data && !dataset.data.currentVersion),
  );
  const profile = useDatasetProfile(
    projectId,
    datasetId,
    Boolean(dataset.data?.currentVersion),
  );

  useEffect(() => {
    if (importStatus.data?.status !== "Completed") return;

    void queryClient.invalidateQueries({
      queryKey: queryKeys.datasets.detail(projectId, datasetId),
    });

    void queryClient.invalidateQueries({
      queryKey: queryKeys.datasets.list(projectId),
    });
  }, [importStatus.data?.status, queryClient, projectId, datasetId]);

  const [activeField, setActiveField] = useState<string | null>(null);
  const field = profile.data?.columns.find(
    (column) => column.name === activeField,
  );
  const { mode } = useSession();
  const [steps, setSteps] = useState<TransformStep[]>(() =>
    mode === "demo" ? mockPreparation.getRecipe(datasetId) : [],
  );

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const hasPreparedPreview = mode === "demo" && steps.length > 0;

  const datasetRows = useDatasetRows(
    projectId,
    datasetId,
    page,
    pageSize,
    Boolean(dataset.data?.currentVersion) && !hasPreparedPreview,
  );

  function saveSteps(next: TransformStep[]) {
    mockPreparation.saveRecipe(datasetId, next);
    setSteps(next);
  }

  const preparedPreview = useQuery({
    queryKey: queryKeys.datasets.preparedPreview(projectId, datasetId, steps),
    queryFn: () => mockPreparation.preview(projectId, datasetId, steps),
    enabled: mode === "demo" && steps.length > 0,
  });

  const apply = useMutation({
    mutationFn: () => mockPreparation.applyRecipe(projectId, datasetId, steps),

    onSuccess: async (dataset) => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.datasets.list(projectId),
      });

      navigate(`/projects/${projectId}/data/${dataset.id}`);
    },
  });

  return (
    <div className="page-content data-page dataset-page">
      <Link className="data-back-link" to={`/projects/${projectId}/data`}>
        <Icon name="back" size={15} /> All data
      </Link>
      {dataset.isPending ? (
        <LoadingState label="Opening dataset…" />
      ) : dataset.isError ? (
        <ErrorState
          error={dataset.error}
          retry={() => void dataset.refetch()}
        />
      ) : (
        <>
          <PageHeading
            eyebrow="DATASET"
            title={dataset.data.name}
            description={
              dataset.data.currentVersion
                ? `${dataset.data.currentVersion.originalFileName} · Imported ${date(dataset.data.createdAtUtc)}`
                : `Imported ${date(dataset.data.createdAtUtc)}`
            }
            actions={
              <Button
                component={Link}
                to={`/projects/${projectId}/explore?dataset=${datasetId}`}
                disabled={!dataset.data.currentVersion}
                leftSection={<Icon name="chart" size={17} />}
              >
                Build a visualization
              </Button>
            }
          />
          {!dataset.data.currentVersion ? (
            <div className="dataset-pending">
              <span className="empty-icon">
                <Icon
                  name={
                    importStatus.data?.status === "Failed" ? "warning" : "clock"
                  }
                  size={30}
                />
              </span>

              <h2>
                {importStatus.data?.status === "Failed"
                  ? "The import could not be completed."
                  : importStatus.data?.status === "Running"
                    ? "Preparing your dataset..."
                    : importStatus.data?.status === "Completed"
                      ? "Your import is complete."
                      : "Your dataset isn't ready yet."}
              </h2>

              <p>
                We'll check the import status automatically. You can leave this
                page and return later.
              </p>

              <Alert
                color={
                  importStatus.isError || importStatus.data?.status === "Failed"
                    ? "red"
                    : "gray"
                }
                role="status"
              >
                {importStatus.isError
                  ? importStatus.error.message
                  : importStatus.data?.status === "Failed"
                    ? (importStatus.data.errorMessage ??
                      "Import failed. Please try again.")
                    : importStatus.data?.status === "Running"
                      ? "The server is processing your CSV file."
                      : importStatus.data?.status === "Completed"
                        ? "Import completed. Loading your dataset..."
                        : importStatus.isFetching
                          ? "Checking import status..."
                          : "Waiting for the import to start."}
              </Alert>

              <Button
                variant="default"
                loading={dataset.isFetching || importStatus.isFetching}
                onClick={() => {
                  void importStatus.refetch();
                  void dataset.refetch();
                }}
              >
                Check again
              </Button>
            </div>
          ) : profile.isPending ? (
            <LoadingState label="Building your data profile…" />
          ) : profile.isError ? (
            <ErrorState
              error={profile.error}
              retry={() => void profile.refetch()}
            />
          ) : (
            <>
              <div className="dataset-metrics">
                <div>
                  <span>Rows</span>
                  <strong>{number.format(profile.data.rowCount)}</strong>
                </div>
                <div>
                  <span>Columns</span>
                  <strong>{number.format(profile.data.columnCount)}</strong>
                </div>
                <div>
                  <span>Cell completeness</span>
                  <strong
                    className={
                      missingCells(profile.data) ? "has-missing" : "is-complete"
                    }
                  >
                    {completeness(profile.data).toFixed(1)}
                    <small>%</small>
                  </strong>
                </div>
                <div>
                  <span>Version</span>
                  <strong className="dataset-version">
                    v{dataset.data.currentVersion.versionNumber}
                    <span className="metric-ready">
                      <i />
                      Ready
                    </span>
                  </strong>
                </div>
              </div>
              {preparedPreview.isError && (
                <ErrorState
                  error={preparedPreview.error}
                  retry={() => void preparedPreview.refetch()}
                />
              )}

              {preparedPreview.isFetching && steps.length > 0 && (
                <div className="dataset-preview-updating">
                  Updating preview…
                </div>
              )}

              <div className="dataset-preview-layout">
                <div>
                  <DataPreview
                    profile={preparedPreview.data ?? profile.data}
                    rows={
                      hasPreparedPreview
                        ? (preparedPreview.data?.previewRows ??
                          profile.data.previewRows)
                        : (datasetRows.data?.items ?? [])
                    }
                    page={
                      hasPreparedPreview ? 1 : (datasetRows.data?.page ?? page)
                    }
                    pageSize={pageSize}
                    totalPages={
                      hasPreparedPreview
                        ? 1
                        : (datasetRows.data?.totalPages ?? 1)
                    }
                    paginated={!hasPreparedPreview}
                    onPageChange={setPage}
                    onPageSizeChange={(size) => {
                      setPageSize(size);
                      setPage(1);
                    }}
                  />
                </div>

                <div className="dataset-right-rail">
                  <DatasetIssuesPanel
                    profile={preparedPreview.data ?? profile.data}
                    selectedField={field}
                    onSelectField={setActiveField}
                    onBackToIssues={() => setActiveField(null)}
                  />

                  <AppliedStepsPanel
                    profile={profile.data}
                    steps={steps}
                    editable={mode === "demo"}
                    onStepsChange={saveSteps}
                  />

                  {mode === "demo" && steps.length > 0 && (
                    <section className="dataset-apply-panel">
                      <div className="data-section-label">PREPARED DATA</div>

                      <p>
                        Create a new dataset with these preparation steps
                        applied. Your source dataset stays unchanged.
                      </p>

                      {apply.isError && (
                        <div className="dataset-apply-error">
                          {apply.error instanceof Error
                            ? apply.error.message
                            : "The prepared copy could not be created."}
                        </div>
                      )}

                      <Button
                        fullWidth
                        leftSection={<Icon name="copy" size={15} />}
                        disabled={
                          !steps.length ||
                          preparedPreview.isFetching ||
                          preparedPreview.isError ||
                          !(preparedPreview.data ?? profile.data).rowCount
                        }
                        loading={apply.isPending}
                        onClick={() => apply.mutate()}
                      >
                        Create prepared copy
                      </Button>
                    </section>
                  )}
                </div>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
