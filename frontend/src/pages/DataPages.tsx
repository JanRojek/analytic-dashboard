import { useMemo, useState } from "react";
import {
  useDataset,
  useDatasetProfile,
  useDatasets,
  useDatasetUsage,
  useDeleteDataset,
} from "../features/data/hooks";
import { queryKeys } from "../data/queryKeys";
import {
  ActionIcon,
  Alert,
  Button,
  Menu,
  Modal,
  Select,
  Tabs,
  TextInput,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Link,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";
import { Icon } from "../components/Icon";
import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../components/Feedback";
import { ImportDataDialog } from "../components/ImportDataDialog";
import { useSession } from "../data/session";
import type {
  ColumnProfile,
  Dataset,
  DatasetProfile,
  TransformStep,
} from "../data/types";
import { mockPreparation } from "../mocks/preparation";
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
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedTab = searchParams.get("tab");
  const tab =
    selectedTab === "quality" || selectedTab === "prepare"
      ? selectedTab
      : "preview";
  const [pollingStarted, setPollingStarted] = useState(() => Date.now());
  const dataset = useDataset(
      projectId,
      datasetId,
      pollingStarted,
  );
  const profile = useDatasetProfile(
      projectId,
      datasetId,
      Boolean(dataset.data?.currentVersion),
  );
  const [activeField, setActiveField] = useState<string | null>(null);
  const field =
    profile.data?.columns.find((column) => column.name === activeField) ??
    profile.data?.columns[0];

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
                <Icon name="clock" size={30} />
              </span>
              <h2>Your dataset isn’t ready yet.</h2>
              <p>
                We’ll check for a ready dataset automatically for about a
                minute. You can leave this page and reopen it from Data.
              </p>
              <Alert color="gray">
                The server hasn’t provided a ready version. Detailed import
                progress and failure messages are not available yet, so we can’t
                tell whether it is still processing or needs attention.
              </Alert>
              <Button
                variant="default"
                loading={dataset.isFetching}
                onClick={() => {
                  setPollingStarted(Date.now());
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
                  <span>Current version</span>
                  <strong>
                    <small>v</small>
                    {dataset.data.currentVersion.versionNumber}
                    <span className="metric-ready">
                      <i />
                      Ready
                    </span>
                  </strong>
                </div>
              </div>
              <Tabs
                value={tab}
                onChange={(value) =>
                  setSearchParams(
                    value === "preview" ? {} : { tab: value ?? "preview" },
                  )
                }
                className="dataset-tabs"
                keepMounted={false}
              >
                <Tabs.List>
                  <Tabs.Tab
                    value="preview"
                    leftSection={<Icon name="data" size={16} />}
                  >
                    Data preview
                  </Tabs.Tab>
                  <Tabs.Tab
                    value="quality"
                    leftSection={<Icon name="shield" size={16} />}
                  >
                    Quality & profile
                    {missingCells(profile.data) > 0 && (
                      <span className="tab-notice-dot" />
                    )}
                  </Tabs.Tab>
                  <Tabs.Tab
                    value="prepare"
                    leftSection={<Icon name="settings" size={16} />}
                  >
                    Prepare data
                  </Tabs.Tab>
                </Tabs.List>
                <Tabs.Panel value="preview">
                  <div className="dataset-preview-layout">
                    <div>
                      <DataPreview profile={profile.data} />
                      <div className="data-next-step">
                        <span>
                          <Icon name="shield" size={18} />
                          <strong>
                            Every good analysis starts with a quick check.
                          </strong>
                        </span>
                        <Button
                          variant="subtle"
                          size="xs"
                          rightSection={<Icon name="arrow" size={14} />}
                          onClick={() => setSearchParams({ tab: "quality" })}
                        >
                          Review data quality
                        </Button>
                      </div>
                    </div>
                    <aside className="data-sidebar-panel">
                      <div className="data-section-label">
                        ABOUT THIS DATASET
                      </div>
                      <dl>
                        <div>
                          <dt>Source</dt>
                          <dd>
                            <Icon name="data" size={14} /> CSV upload
                          </dd>
                        </div>
                        <div>
                          <dt>Fields</dt>
                          <dd>
                            {
                              profile.data.columns.filter(
                                (column) => column.type === "number",
                              ).length
                            }{" "}
                            numeric
                            <br />
                            {
                              profile.data.columns.filter(
                                (column) => column.type === "text",
                              ).length
                            }{" "}
                            text
                            <br />
                            {
                              profile.data.columns.filter(
                                (column) => column.type === "date",
                              ).length
                            }{" "}
                            date
                          </dd>
                        </div>
                        <div>
                          <dt>Coverage</dt>
                          <dd>
                            Profile covers all{" "}
                            {number.format(profile.data.rowCount)} rows
                          </dd>
                        </div>
                      </dl>
                      <div className="data-sidebar-tip">
                        <Icon name="bolt" size={18} />
                        <strong>A useful starting point</strong>
                        <p>
                          Group a text field and add a numeric measure to reveal
                          your first pattern.
                        </p>
                        <Link
                          to={`/projects/${projectId}/explore?dataset=${datasetId}`}
                        >
                          Explore this dataset <Icon name="arrow" size={14} />
                        </Link>
                      </div>
                    </aside>
                  </div>
                </Tabs.Panel>
                <Tabs.Panel value="quality">
                  <div className="quality-summary">
                    <span
                      className={`quality-summary-icon ${missingCells(profile.data) ? "attention" : ""}`}
                    >
                      <Icon
                        name={missingCells(profile.data) ? "warning" : "check"}
                        size={22}
                      />
                    </span>
                    <div>
                      <h2>
                        {missingCells(profile.data)
                          ? "A few gaps worth a closer look."
                          : "A complete starting point."}
                      </h2>
                      <p>
                        {missingCells(profile.data)
                          ? `${number.format(missingCells(profile.data))} empty cells across ${profile.data.columns.filter((column) => column.nullCount > 0).length} fields. Review them before choosing your measures.`
                          : "Every cell has a value. Review the detected types and ranges to check that your data makes sense."}
                      </p>
                    </div>
                    {missingCells(profile.data) > 0 && (
                      <Button
                        variant="default"
                        onClick={() => setSearchParams({ tab: "prepare" })}
                      >
                        Prepare data <Icon name="arrow" size={15} />
                      </Button>
                    )}
                  </div>
                  <div className="profile-layout">
                    <div className="data-table-card">
                      <div
                        className="data-table-scroll"
                        tabIndex={0}
                        aria-label="Column quality profiles"
                      >
                        <table className="data-table profile-table">
                          <thead>
                            <tr>
                              <th>Field</th>
                              <th>Detected type</th>
                              <th>Completeness</th>
                              <th>Empty cells</th>
                            </tr>
                          </thead>
                          <tbody>
                            {profile.data.columns.map((column) => {
                              const complete = profile.data.rowCount
                                ? (1 -
                                    column.nullCount / profile.data.rowCount) *
                                  100
                                : 100;
                              return (
                                <tr
                                  className={
                                    field?.name === column.name
                                      ? "selected-field"
                                      : ""
                                  }
                                  key={column.name}
                                >
                                  <td>
                                    <button
                                      className="profile-field-button"
                                      aria-pressed={field?.name === column.name}
                                      onClick={() =>
                                        setActiveField(column.name)
                                      }
                                    >
                                      <Icon
                                        name={fieldIcon(column.type)}
                                        size={16}
                                      />
                                      {column.name}
                                    </button>
                                  </td>
                                  <td>
                                    <span className="field-type-label">
                                      {column.type}
                                    </span>
                                  </td>
                                  <td>
                                    <div className="quality-bar-cell">
                                      <div
                                        className="quality-bar"
                                        aria-label={`${complete.toFixed(1)}% complete`}
                                      >
                                        <span
                                          style={{ width: `${complete}%` }}
                                        />
                                      </div>
                                      <span>{complete.toFixed(1)}%</span>
                                    </div>
                                  </td>
                                  <td>
                                    <span
                                      className={
                                        column.nullCount
                                          ? "missing-count"
                                          : "data-muted"
                                      }
                                    >
                                      {number.format(column.nullCount)}
                                    </span>
                                  </td>
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      </div>
                    </div>
                    {field && (
                      <aside className="data-sidebar-panel field-inspector">
                        <div className="data-section-label">FIELD PROFILE</div>
                        <div className="field-inspector-title">
                          <span>
                            <Icon name={fieldIcon(field.type)} size={22} />
                          </span>
                          <h3>{field.name}</h3>
                        </div>
                        <span className="field-type-label">{field.type}</span>
                        <dl>
                          <div>
                            <dt>Filled values</dt>
                            <dd>
                              {number.format(
                                profile.data.rowCount - field.nullCount,
                              )}
                            </dd>
                          </div>
                          <div>
                            <dt>Empty values</dt>
                            <dd>{number.format(field.nullCount)}</dd>
                          </div>
                          <div>
                            <dt>Minimum</dt>
                            <dd>{field.min ?? "Not available"}</dd>
                          </div>
                          <div>
                            <dt>Maximum</dt>
                            <dd>{field.max ?? "Not available"}</dd>
                          </div>
                          {field.type === "number" && (
                            <div>
                              <dt>Average</dt>
                              <dd>
                                {field.avg === null
                                  ? "Not available"
                                  : number.format(field.avg)}
                              </dd>
                            </div>
                          )}
                        </dl>
                        <p className="field-profile-note">
                          Completeness measures missing values. It does not
                          validate accuracy or detect duplicates.
                        </p>
                      </aside>
                    )}
                  </div>
                </Tabs.Panel>
                <Tabs.Panel value="prepare">
                  <PreparationPanel
                    key={datasetId}
                    projectId={projectId}
                    datasetId={datasetId}
                    profile={profile.data}
                  />
                </Tabs.Panel>
              </Tabs>
            </>
          )}
        </>
      )}
    </div>
  );
}

function DataPreview({
  profile,
  title = "A closer look at your data",
}: {
  profile: DatasetProfile;
  title?: string;
}) {
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<{
    column: string;
    ascending: boolean;
  } | null>(null);
  const rows = useMemo(() => {
    const filtered = profile.previewRows
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
  }, [profile, search, sort]);
  return (
    <section className="data-preview-card">
      <div className="data-preview-toolbar">
        <div>
          <h2>{title}</h2>
          <p>
            First {profile.previewRows.length} of{" "}
            {number.format(profile.rowCount)} rows
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
                <td className="row-number">{index + 1}</td>
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
        <Icon name="eye" size={14} />
        <span>Search and sorting apply to this preview only.</span>
        <span>{profile.columnCount} columns</span>
      </div>
    </section>
  );
}

const transformations = [
  { value: "trim", label: "Trim whitespace" },
  { value: "fill", label: "Fill empty values" },
  { value: "drop-empty", label: "Remove rows with empty values" },
  { value: "deduplicate", label: "Remove duplicate rows" },
];

function PreparationPanel({
  projectId,
  datasetId,
  profile,
}: {
  projectId: string;
  datasetId: string;
  profile: DatasetProfile;
}) {
  const { mode, startDemo } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [steps, setSteps] = useState<TransformStep[]>(() =>
    mode === "demo" ? mockPreparation.getRecipe(datasetId) : [],
  );
  const [kind, setKind] = useState<TransformStep["kind"]>("trim");
  const [column, setColumn] = useState(profile.columns[0]?.name ?? "");
  const [value, setValue] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [demoConfirmation, setDemoConfirmation] = useState(false);
  const preview = useQuery({
    queryKey: queryKeys.datasets.preparedPreview(
        projectId,
        datasetId,
        steps,
    ),
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
  const prepared = steps.length ? preview.data : profile;

  function saveSteps(next: TransformStep[]) {
    try {
      mockPreparation.saveRecipe(datasetId, next);
      setSteps(next);
      setEditError(null);
      apply.reset();
    } catch (error) {
      setEditError(
        error instanceof Error
          ? error.message
          : "Your preparation steps could not be saved.",
      );
    }
  }

  if (mode !== "demo")
    return (
      <>
        <div className="preparation-unavailable">
          <span className="empty-icon">
            <Icon name="settings" size={26} />
          </span>
          <span className="data-feature-label">PRODUCT PREVIEW</span>
          <h2>Make your data analysis-ready.</h2>
          <p>
            Build a sequence of preparation steps, preview the result, and keep
            your original data intact. Preparation is available in the demo
            workspace while server support is being built.
          </p>
          <div className="preparation-capabilities">
            <span>
              <Icon name="check" size={16} /> Trim whitespace
            </span>
            <span>
              <Icon name="check" size={16} /> Fill empty values
            </span>
            <span>
              <Icon name="check" size={16} /> Remove duplicates
            </span>
          </div>
          <Button onClick={() => setDemoConfirmation(true)}>
            Try preparation in the demo
          </Button>
          <small>This project's data will stay in your account.</small>
        </div>
        <Modal
          opened={demoConfirmation}
          onClose={() => setDemoConfirmation(false)}
          title="Open the demo workspace?"
          centered
        >
          <p className="data-dialog-description">
            You’ll switch to a separate local workspace with sample data. Your
            account’s projects remain unchanged. Sign in again to return to
            them.
          </p>
          <div className="dialog-footer">
            <Button
              variant="default"
              onClick={() => setDemoConfirmation(false)}
            >
              Stay here
            </Button>
            <Button
              onClick={() => {
                startDemo();
                navigate("/projects");
              }}
            >
              Open demo
            </Button>
          </div>
        </Modal>
      </>
    );

  return (
    <div className="preparation-workspace">
      <div className="preparation-intro">
        <div>
          <h2>A few steps to better data.</h2>
          <p>
            Preview your changes, then create a prepared copy. Your original
            stays intact.
          </p>
        </div>
        <span className="data-feature-label">LOCAL DEMO</span>
      </div>
      <div className="preparation-layout">
        <aside className="preparation-recipe">
          <div className="recipe-header">
            <h3>Preparation steps</h3>
            <span>
              {steps.length} {steps.length === 1 ? "step" : "steps"}
            </span>
          </div>
          {steps.length ? (
            <ol className="recipe-steps">
              {steps.map((step, index) => (
                <li key={step.id}>
                  <span className="recipe-step-number">{index + 1}</span>
                  <div>
                    <strong>
                      {
                        transformations.find((item) => item.value === step.kind)
                          ?.label
                      }
                    </strong>
                    <small>
                      {step.kind === "deduplicate"
                        ? "Compare all fields"
                        : step.column}
                      {step.kind === "fill" ? ` → ${step.value}` : ""}
                    </small>
                  </div>
                  <ActionIcon
                    variant="subtle"
                    color="gray"
                    disabled={apply.isPending}
                    aria-label={`Remove preparation step ${index + 1}`}
                    onClick={() =>
                      saveSteps(steps.filter((item) => item.id !== step.id))
                    }
                  >
                    <Icon name="close" size={14} />
                  </ActionIcon>
                </li>
              ))}
            </ol>
          ) : (
            <div className="recipe-empty">
              <Icon name="settings" size={23} />
              <p>
                No steps yet.
                <br />
                Start with one small improvement.
              </p>
            </div>
          )}
          <form
            className="recipe-form"
            onSubmit={(event) => {
              event.preventDefault();
              if (kind !== "deduplicate" && !column) return;
              if (kind === "fill" && !value.trim()) return;
              saveSteps([
                ...steps,
                {
                  id: crypto.randomUUID(),
                  kind,
                  column: kind === "deduplicate" ? "" : column,
                  ...(kind === "fill" ? { value } : {}),
                },
              ]);
              setValue("");
            }}
          >
            <div className="data-section-label">ADD A STEP</div>
            <Select
              label="Operation"
              value={kind}
              data={transformations}
              allowDeselect={false}
              onChange={(next) => setKind(next as TransformStep["kind"])}
              disabled={apply.isPending}
            />
            {kind !== "deduplicate" && (
              <Select
                label="Field"
                data={profile.columns.map((item) => item.name)}
                value={column}
                onChange={(next) => setColumn(next ?? "")}
                searchable
                allowDeselect={false}
                disabled={apply.isPending}
              />
            )}
            {kind === "fill" && (
              <TextInput
                label="Replace empty values with"
                placeholder="e.g. Unknown or 0"
                value={value}
                onChange={(event) => setValue(event.currentTarget.value)}
                required
                disabled={apply.isPending}
              />
            )}
            <p className="recipe-operation-hint">
              {kind === "trim"
                ? "Remove spaces from the beginning and end of values."
                : kind === "fill"
                  ? "Use a replacement value wherever this field is empty."
                  : kind === "drop-empty"
                    ? "Exclude rows where the selected field has no value."
                    : "Keep the first instance of each identical row."}
            </p>
            <Button
              type="submit"
              variant="default"
              leftSection={<Icon name="plus" size={15} />}
              fullWidth
              disabled={
                apply.isPending ||
                (kind !== "deduplicate" && !column) ||
                (kind === "fill" && !value.trim())
              }
            >
              Add step
            </Button>
          </form>
          <div className="recipe-saved">
            <Icon name="check" size={13} /> Steps saved in this browser
          </div>
        </aside>
        <div className="preparation-results">
          {(editError || apply.error) && (
            <ErrorState
              error={editError ? new Error(editError) : apply.error}
            />
          )}
          {preview.isError ? (
            <ErrorState
              error={preview.error}
              retry={() => void preview.refetch()}
            />
          ) : steps.length > 0 && preview.isPending ? (
            <LoadingState label="Previewing your preparation steps…" />
          ) : (
            prepared && (
              <>
                <div className="preparation-comparison">
                  <div>
                    <span>Rows after preparation</span>
                    <strong>
                      {number.format(prepared.rowCount)}{" "}
                      <small>of {number.format(profile.rowCount)}</small>
                    </strong>
                  </div>
                  <div>
                    <span>Empty cells</span>
                    <strong>
                      {number.format(missingCells(prepared))}{" "}
                      <small>was {number.format(missingCells(profile))}</small>
                    </strong>
                  </div>
                  <div>
                    <span>Completeness</span>
                    <strong>
                      {completeness(prepared).toFixed(1)}
                      <small>%</small>
                    </strong>
                  </div>
                </div>
                <>
                  {!prepared.rowCount && (
                    <Alert color="orange" icon={<Icon name="warning" />}>
                      These steps remove every row. Adjust your recipe before
                      creating a prepared copy.
                    </Alert>
                  )}
                </>
                <DataPreview
                  profile={prepared}
                  title={
                    steps.length
                      ? "Your prepared data"
                      : "Original data preview"
                  }
                />
                <div className="preparation-apply">
                  <p>
                    <Icon name="copy" size={17} />
                    <span>
                      Creates a new dataset with all steps applied.
                      <br />
                      <small>
                        The source dataset and its charts stay unchanged.
                      </small>
                    </span>
                  </p>
                  <Button
                    leftSection={<Icon name="check" size={16} />}
                    disabled={
                      !steps.length ||
                      preview.isFetching ||
                      preview.isError ||
                      !prepared.rowCount
                    }
                    loading={apply.isPending}
                    onClick={() => apply.mutate()}
                  >
                    Create prepared copy
                  </Button>
                </div>
              </>
            )
          )}
        </div>
      </div>
    </div>
  );
}
