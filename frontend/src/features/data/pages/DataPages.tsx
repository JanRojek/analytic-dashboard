import { useMemo, useState, useEffect } from "react";

import {
  useDataset,
  useDatasetProfile,
  useDatasetRows,
  useDatasets,
  useDatasetUsage,
  useDeleteDataset,
  useImportStatus,
} from "../hooks";

import { queryKeys } from "../../../data/queryKeys";

import {
  ActionIcon,
  Alert,
  Button,
  Menu,
  Modal,
  Pagination,
  Select,
  TextInput,
} from "@mantine/core";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { Link, useNavigate, useParams } from "react-router-dom";

import { Icon } from "../../../components/Icon";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";

import { ImportDataDialog } from "../components/ImportDataDialog";
import { useSession } from "../../../session/SessionContext";

import { DatasetIssuesPanel } from "../components/DatasetIssuesPanel";

import type {
  ColumnProfile,
  DataRow,
  Dataset,
  DatasetProfile,
  TransformStep,
} from "../../../data/types";

import { mockPreparation } from "../../../mocks/preparation";

import { AppliedStepsPanel } from "../components/AppliedStepsPanel";

import "../styles/data.css";

const number = new Intl.NumberFormat("en", { maximumFractionDigits: 2 });
const date = (value: string) =>
  new Date(value).toLocaleDateString("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
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
const fieldIcon = (type: ColumnProfile["type"]) =>
  type === "number" ? "number" : type === "date" ? "calendar" : "text";

export function DataPage() {
  const { projectId = "" } = useParams();

  const [importOpen, setImportOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [deleting, setDeleting] = useState<Dataset | null>(null);

  const datasets = useDatasets(projectId);
  const usage = useDatasetUsage(projectId, deleting?.id);
  const remove = useDeleteDataset(projectId);

  const visible =
    datasets.data?.filter((item) =>
      item.name.toLowerCase().includes(search.toLowerCase()),
    ) ?? [];

  const ready = datasets.data?.filter((item) => item.currentVersion) ?? [];

  return (
    <div className="page-content data-page">
      <PageHeading
        eyebrow="YOUR PROJECT'S FOUNDATION"
        title="Data"
        description="Bring your data together. Understand it before you build with it."
        actions={
          <Button
            leftSection={<Icon name="plus" size={17} />}
            onClick={() => setImportOpen(true)}
          >
            Add data
          </Button>
        }
      />
      <div className="data-workflow-banner">
        <div className="data-workflow-step">
          <span>01</span>
          <div>
            <strong>Bring it in</strong>
            <p>Turn a CSV into a dataset.</p>
          </div>
        </div>
        <Icon name="arrow" size={18} />
        <div className="data-workflow-step">
          <span>02</span>
          <div>
            <strong>Get to know it</strong>
            <p>Check columns and completeness.</p>
          </div>
        </div>
        <Icon name="arrow" size={18} />
        <div className="data-workflow-step">
          <span>03</span>
          <div>
            <strong>Find your story</strong>
            <p>Prepare, explore, and visualize.</p>
          </div>
        </div>
      </div>
      {datasets.isPending ? (
        <LoadingState label="Loading datasets…" />
      ) : datasets.isError ? (
        <ErrorState
          error={datasets.error}
          retry={() => void datasets.refetch()}
        />
      ) : !datasets.data.length ? (
        <EmptyState
          icon="data"
          title="Good questions start with data."
          description="Add your first CSV to inspect its columns, check data quality, and start finding patterns."
          action={
            <Button
              leftSection={<Icon name="upload" size={17} />}
              onClick={() => setImportOpen(true)}
            >
              Import your first dataset
            </Button>
          }
        />
      ) : (
        <>
          <div className="data-list-heading">
            <div>
              <h2>
                All datasets <span>{datasets.data.length}</span>
              </h2>
              <p>
                {number.format(
                  ready.reduce(
                    (total, item) =>
                      total + (item.currentVersion?.rowCount ?? 0),
                    0,
                  ),
                )}{" "}
                rows across your project
              </p>
            </div>
            <TextInput
              placeholder="Find a dataset…"
              aria-label="Find a dataset"
              value={search}
              onChange={(event) => setSearch(event.currentTarget.value)}
              leftSection={<Icon name="search" size={16} />}
            />
          </div>
          <div className="data-table-card">
            <div
              className="data-table-scroll"
              tabIndex={0}
              aria-label="Project datasets"
            >
              <table className="data-table datasets-table">
                <thead>
                  <tr>
                    <th>Dataset</th>
                    <th>Rows</th>
                    <th>Columns</th>
                    <th>Status</th>
                    <th>Added</th>
                    <th>
                      <span className="sr-only">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map((dataset) => (
                    <tr key={dataset.id}>
                      <td>
                        <Link
                          to={`/projects/${projectId}/data/${dataset.id}`}
                          className="dataset-name-link"
                        >
                          <span className="data-file-symbol">
                            <Icon name="data" size={20} />
                          </span>
                          <span>
                            <strong>{dataset.name}</strong>
                            <small>
                              {dataset.currentVersion?.originalFileName ??
                                "CSV import"}
                            </small>
                          </span>
                        </Link>
                      </td>
                      <td>
                        {dataset.currentVersion
                          ? number.format(dataset.currentVersion.rowCount)
                          : "—"}
                      </td>
                      <td>{dataset.currentVersion?.columnCount ?? "—"}</td>
                      <td>
                        <span
                          className={`data-status ${dataset.currentVersion ? "" : "pending"}`}
                        >
                          <i />
                          {dataset.currentVersion
                            ? "Ready to explore"
                            : "Not yet ready"}
                        </span>
                      </td>
                      <td className="data-muted">
                        {date(dataset.createdAtUtc)}
                      </td>
                      <td>
                        <Menu position="bottom-end" withinPortal>
                          <Menu.Target>
                            <ActionIcon
                              variant="subtle"
                              color="gray"
                              aria-label={`Actions for ${dataset.name}`}
                            >
                              <Icon name="more" />
                            </ActionIcon>
                          </Menu.Target>
                          <Menu.Dropdown>
                            <Menu.Item
                              component={Link}
                              to={`/projects/${projectId}/data/${dataset.id}`}
                              leftSection={<Icon name="eye" size={16} />}
                            >
                              Inspect dataset
                            </Menu.Item>
                            <Menu.Item
                              component={Link}
                              to={`/projects/${projectId}/explore?dataset=${dataset.id}`}
                              disabled={!dataset.currentVersion}
                              leftSection={<Icon name="chart" size={16} />}
                            >
                              Build a visualization
                            </Menu.Item>
                            <Menu.Divider />
                            <Menu.Item
                              color="red"
                              leftSection={<Icon name="trash" size={16} />}
                              onClick={() => {
                                remove.reset();
                                setDeleting(dataset);
                              }}
                            >
                              Delete dataset
                            </Menu.Item>
                          </Menu.Dropdown>
                        </Menu>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {!visible.length && (
                <div className="data-no-results">
                  No datasets match “{search}”.{" "}
                  <button onClick={() => setSearch("")}>Clear search</button>
                </div>
              )}
            </div>
          </div>
          <p className="data-footnote">
            <Icon name="lock" size={14} /> Datasets belong to this project and
            can power multiple dashboards.
          </p>
        </>
      )}
      <ImportDataDialog
        projectId={projectId}
        opened={importOpen}
        onClose={() => setImportOpen(false)}
      />
      <Modal
        opened={!!deleting}
        onClose={() => {
          if (!remove.isPending) setDeleting(null);
        }}
        title="Delete this dataset?"
        centered
        closeOnClickOutside={!remove.isPending}
        closeOnEscape={!remove.isPending}
        withCloseButton={!remove.isPending}
      >
        <p className="data-dialog-description">
          “{deleting?.name}” and its data will be permanently removed. This
          cannot be undone.
        </p>
        {usage.isPending && (
          <p className="data-dialog-description" role="status">
            Checking whether dashboards use this dataset…
          </p>
        )}
        {usage.isError && (
          <ErrorState error={usage.error} retry={() => void usage.refetch()} />
        )}
        {!!usage.data?.length && (
          <Alert
            color="orange"
            title="This dataset is still in use"
            icon={<Icon name="warning" />}
          >
            Remove its charts from these dashboards before deleting the dataset:
            <ul className="dataset-usage-list">
              {usage.data.map(({ dashboard, count }) => (
                <li key={dashboard.id}>
                  <Link
                    to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                  >
                    {dashboard.name}
                  </Link>{" "}
                  · {count} {count === 1 ? "chart" : "charts"}
                </li>
              ))}
            </ul>
          </Alert>
        )}
        {remove.error && <ErrorState error={remove.error} />}
        <div className="dialog-footer">
          <Button
            variant="default"
            disabled={remove.isPending}
            onClick={() => setDeleting(null)}
          >
            Keep dataset
          </Button>
          <Button
            color="red"
            loading={remove.isPending}
            disabled={
              usage.isPending ||
              usage.isError ||
              usage.isFetching ||
              !!usage.data?.length
            }
            onClick={() => {
              if (deleting) {
                remove.mutate(deleting.id, {
                  onSuccess: () => setDeleting(null),
                });
              }
            }}
          >
            Delete dataset
          </Button>
        </div>
      </Modal>
    </div>
  );
}

export function DatasetPage() {
  const { projectId = "", datasetId = "" } = useParams();
  return (
    <DatasetWorkspace
      key={`${projectId}:${datasetId}`}
      projectId={projectId}
      datasetId={datasetId}
    />
  );
}

function DatasetWorkspace({
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

function DataPreview({
  profile,
  rows: sourceRows,
  page,
  pageSize,
  totalPages,
  paginated,
  onPageChange,
  onPageSizeChange,
  title = "Data",
}: {
  profile: DatasetProfile;
  rows: DataRow[];
  page: number;
  pageSize: number;
  totalPages: number;
  paginated: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  title?: string;
}) {
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<{
    column: string;
    ascending: boolean;
  } | null>(null);
  const rows = useMemo(() => {
    const filtered = sourceRows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) =>
        Object.values(row).some((value) =>
          String(value ?? "")
            .toLowerCase()
            .includes(search.toLowerCase()),
        ),
      );
    if (!sort) return filtered;
    const type = profile.columns.find(
      (column) => column.name === sort.column,
    )?.type;
    return filtered.sort((a, b) => {
      const left = a.row[sort.column];
      const right = b.row[sort.column];
      const comparison =
        type === "number" && left !== null && right !== null
          ? Number(left) - Number(right)
          : String(left ?? "").localeCompare(String(right ?? ""));
      return sort.ascending ? comparison : -comparison;
    });
  }, [sourceRows, profile.columns, search, sort]);
  return (
    <section className="data-preview-card">
      <div className="data-preview-toolbar">
        <div>
          <h2>{title}</h2>
          <p>
            {number.format(profile.rowCount)} rows ·{" "}
            {number.format(profile.columnCount)} columns
          </p>
        </div>
        <TextInput
          placeholder="Search preview…"
          aria-label="Search preview rows"
          size="xs"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          leftSection={<Icon name="search" size={14} />}
        />
      </div>
      <div
        className="data-table-scroll preview-scroll"
        tabIndex={0}
        aria-label="Dataset row preview"
      >
        <table className="data-table preview-table">
          <thead>
            <tr>
              <th className="row-number">#</th>
              {profile.columns.map((column) => (
                <th
                  key={column.name}
                  aria-sort={
                    sort?.column === column.name
                      ? sort.ascending
                        ? "ascending"
                        : "descending"
                      : "none"
                  }
                >
                  <button
                    className="preview-column-button"
                    onClick={() =>
                      setSort({
                        column: column.name,
                        ascending:
                          sort?.column === column.name ? !sort.ascending : true,
                      })
                    }
                  >
                    <Icon name={fieldIcon(column.type)} size={14} />
                    <span>{column.name}</span>
                    {sort?.column === column.name && (
                      <span aria-hidden="true">
                        {sort.ascending ? "↑" : "↓"}
                      </span>
                    )}
                  </button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(({ row, index }) => (
              <tr key={index}>
                <td className="row-number">
                  {paginated ? (page - 1) * pageSize + index + 1 : index + 1}
                </td>
                {profile.columns.map((column) => (
                  <td
                    key={column.name}
                    className={column.type === "number" ? "numeric-cell" : ""}
                  >
                    {row[column.name] === null ||
                    row[column.name] === undefined ||
                    row[column.name] === "" ? (
                      <span className="null-value">empty</span>
                    ) : (
                      row[column.name]
                    )}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        {!rows.length && (
          <div className="data-no-results">
            {search
              ? "No preview rows match your search."
              : "This dataset has no rows."}
          </div>
        )}
      </div>
      <div className="data-preview-footer">
        <div className="data-preview-note">
          <Icon name="eye" size={14} />
          <span>Search and sorting apply to this page only.</span>
        </div>

        {paginated && (
          <div className="data-preview-controls">
            {totalPages > 1 && (
              <Pagination
                value={page}
                onChange={onPageChange}
                total={totalPages}
                size="sm"
              />
            )}

            <div className="data-page-size">
              <span>Rows per page</span>

              <Select
                size="xs"
                w={78}
                value={String(pageSize)}
                data={["25", "50", "100"]}
                onChange={(value) => {
                  if (value) {
                    onPageSizeChange(Number(value));
                  }
                }}
              />
            </div>
          </div>
        )}

        <span className="data-preview-columns">
          {profile.columnCount} columns
        </span>
      </div>
    </section>
  );
}
