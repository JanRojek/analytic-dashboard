import { useEffect, useState } from "react";
import {
  useDashboard,
  useDashboards,
  useDashboardWidgets,
  useCreateDashboard,
  useDeleteDashboard,
  useDeleteWidget,
} from "../features/dashboards/hooks";
import { queryKeys } from "../data/queryKeys";
import {
  ActionIcon,
  Button,
  Menu,
  Modal,
  SegmentedControl,
  Select,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Link,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";
import { Brand } from "../components/Brand";
import { Chart } from "../components/Charts";
import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../components/Feedback";
import { Icon, type IconName } from "../components/Icon";
import { useSession } from "../data/session";
import type {
  Aggregation,
  Dashboard,
  Dataset,
  DatasetProfile,
  QueryConfig,
  QueryItem,
  Widget,
  WidgetInput,
  WidgetType,
} from "../data/types";
import {
  editorScope,
  persistEditorDraft,
  readEditor,
  readExploration,
  removeEditor,
  saveEditor,
  saveExploration,
  type EditorDocument,
} from "../mocks/editor";
import "../styles/analytics.css";

const chartTypes: {
  value: WidgetType;
  label: string;
  icon: IconName;
  description: string;
}[] = [
  {
    value: "BarChart",
    label: "Bar",
    icon: "chart",
    description: "Compare categories",
  },
  {
    value: "LineChart",
    label: "Line",
    icon: "line",
    description: "Follow a trend",
  },
  {
    value: "PieChart",
    label: "Donut",
    icon: "pie",
    description: "See the composition",
  },
  {
    value: "Kpi",
    label: "Number",
    icon: "number",
    description: "Focus on a metric",
  },
];
const aggregations: Aggregation[] = ["Sum", "Average", "Count", "Min", "Max"];
const formatName = (value: string) =>
  value.replace(/_/g, " ").replace(/^./, (char) => char.toUpperCase());
function defaults(profile: DatasetProfile): QueryConfig {
  const numeric = profile.columns.find((column) => column.type === "number");
  // Prefer a manageable categorical comparison to a high-cardinality date or ID.
  const categories = profile.columns.filter((column) => column.type === "text");
  const group =
    categories.find((column) => {
      const distinct = new Set(
        profile.previewRows.map((row) => row[column.name]).filter(Boolean),
      );
      return distinct.size > 1 && distinct.size <= 12;
    }) ||
    categories[0] ||
    profile.columns.find((column) => column.type === "date") ||
    profile.columns[0];
  return {
    groupByColumn: group?.name || "",
    measureColumn: (numeric || profile.columns[0])?.name || "",
    aggregation: numeric ? "Sum" : "Count",
  };
}
function queryCaption(config: QueryConfig) {
  return `${config.aggregation === "Count" ? "Count of nonempty" : config.aggregation} ${formatName(config.measureColumn).toLowerCase()} by ${formatName(config.groupByColumn).toLowerCase()}`;
}
function ChartTypePicker({
  value,
  onChange,
}: {
  value: WidgetType;
  onChange: (value: WidgetType) => void;
}) {
  return (
    <div className="chart-type-picker" role="group" aria-label="Chart type">
      {chartTypes.map((type) => (
        <Tooltip key={type.value} label={type.description}>
          <button
            type="button"
            aria-pressed={value === type.value}
            onClick={() => onChange(type.value)}
          >
            <Icon name={type.icon} size={20} />
            <span>{type.label}</span>
          </button>
        </Tooltip>
      ))}
    </div>
  );
}
function QueryFields({
  profile,
  config,
  onChange,
}: {
  profile: DatasetProfile;
  config: QueryConfig;
  onChange: (value: QueryConfig) => void;
}) {
  const measures = profile.columns.filter(
    (column) => config.aggregation === "Count" || column.type === "number",
  );
  return (
    <div className="query-fields">
      <Select
        label="Group by"
        description="The categories along your chart"
        data={profile.columns.map((column) => ({
          value: column.name,
          label: formatName(column.name),
        }))}
        value={config.groupByColumn}
        onChange={(value) =>
          value && onChange({ ...config, groupByColumn: value })
        }
        allowDeselect={false}
        searchable
      />
      <div className="query-field-divider" />
      <Select
        label="Measure"
        data={measures.map((column) => ({
          value: column.name,
          label: formatName(column.name),
        }))}
        value={config.measureColumn}
        onChange={(value) =>
          value && onChange({ ...config, measureColumn: value })
        }
        allowDeselect={false}
        searchable
      />
      <Select
        label="Calculate"
        data={aggregations.map((value) => ({
          value,
          label:
            value === "Count"
              ? "Count nonempty values"
              : value === "Min"
                ? "Minimum"
                : value === "Max"
                  ? "Maximum"
                  : value,
        }))}
        value={config.aggregation}
        onChange={(value) => {
          if (!value) return;
          const aggregation = value as Aggregation;
          const measure = profile.columns.find(
            (column) => column.name === config.measureColumn,
          );
          onChange({
            ...config,
            aggregation,
            measureColumn:
              aggregation !== "Count" && measure?.type !== "number"
                ? profile.columns.find((column) => column.type === "number")
                    ?.name || ""
                : config.measureColumn,
          });
        }}
        allowDeselect={false}
      />
      {!measures.length && (
        <p className="analytics-note">
          This dataset has no numeric columns. Use Count to count nonempty
          values.
        </p>
      )}
    </div>
  );
}
function ResultsTable({
  items,
  config,
}: {
  items: QueryItem[];
  config: QueryConfig;
}) {
  return (
    <div className="results-table">
      <table>
        <caption className="sr-only">
          Query results: {queryCaption(config)}
        </caption>
        <thead>
          <tr>
            <th scope="col">{formatName(config.groupByColumn)}</th>
            <th scope="col">
              {config.aggregation} · {formatName(config.measureColumn)}
            </th>
          </tr>
        </thead>
        <tbody>
          {items.map((item, index) => (
            <tr key={index}>
              <td>{item.label ?? <span className="muted">(empty)</span>}</td>
              <td>
                {item.value.toLocaleString("en", { maximumFractionDigits: 2 })}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function ExplorePage() {
  const { projectId = "" } = useParams();
  const { adapter, mode, session } = useSession();
  const [params, setParams] = useSearchParams();
  const scope = editorScope(mode, session?.user.id || "", projectId);
  const draft = readExploration(scope);
  const datasets = useQuery({
    queryKey: queryKeys.datasets.list(projectId),
    queryFn: () => adapter.listDatasets(projectId),
  });
  const selected = params.get("dataset") || draft?.datasetId;
  const dataset =
    datasets.data?.find((item) => item.id === selected) ||
    datasets.data?.find((item) => item.currentVersion);
  const profile = useQuery({
    queryKey: queryKeys.datasets.profile(projectId, dataset?.id),
    queryFn: () => adapter.getProfile(projectId, dataset!.id),
    enabled: Boolean(dataset),
  });
  return (
    <div className="page-content analytics-page">
      <PageHeading
        eyebrow="FROM DATA TO INSIGHT"
        title="Ask your data a question."
        description="Start with a comparison. Shape it into a chart. Give it a place on your dashboard."
      />
      {datasets.isPending ? (
        <LoadingState />
      ) : datasets.isError ? (
        <ErrorState error={datasets.error} retry={() => datasets.refetch()} />
      ) : !dataset ? (
        <EmptyState
          icon="chart"
          title="Every insight starts with data"
          description="Add a dataset to your project, then explore its patterns here."
          action={
            <Button component={Link} to={`/projects/${projectId}/data`}>
              Go to data <Icon name="arrow" />
            </Button>
          }
        />
      ) : profile.isPending ? (
        <LoadingState label="Reading the dataset columns…" />
      ) : profile.isError ? (
        <ErrorState error={profile.error} retry={() => profile.refetch()} />
      ) : (
        <ExplorationWorkspace
          key={`${scope}:${dataset.id}:${dataset.currentVersion?.id}`}
          projectId={projectId}
          scope={scope}
          dataset={dataset}
          datasets={datasets.data || []}
          profile={profile.data}
          onDataset={(id) => setParams({ dataset: id })}
        />
      )}
    </div>
  );
}

function ExplorationWorkspace({
  projectId,
  scope,
  dataset,
  datasets,
  profile,
  onDataset,
}: {
  projectId: string;
  scope: string;
  dataset: Dataset;
  datasets: Dataset[];
  profile: DatasetProfile;
  onDataset: (id: string) => void;
}) {
  const { adapter } = useSession();
  const [config, setConfig] = useState<WidgetInput>(() => {
    const draft = readExploration(scope);
    const usable =
      draft?.datasetId === dataset.id &&
      profile.columns.some((column) => column.name === draft.groupByColumn) &&
      profile.columns.some((column) => column.name === draft.measureColumn);
    return usable
      ? draft
      : {
          datasetId: dataset.id,
          ...defaults(profile),
          type: "BarChart",
          title: "",
        };
  });
  const [run, setRun] = useState<QueryConfig>({
    groupByColumn: config.groupByColumn,
    measureColumn: config.measureColumn,
    aggregation: config.aggregation,
  });
  const [view, setView] = useState("chart");
  const [saving, setSaving] = useState(false);
  const [storageError, setStorageError] = useState("");
  const results = useQuery({
    queryKey: queryKeys.analytics.exploration(
        projectId,
        dataset.id,
        dataset.currentVersion?.id,
        run,
    ),
    queryFn: () => adapter.query(projectId, dataset.id, run),
    enabled: Boolean(run.groupByColumn && run.measureColumn),
  });
  const changed =
    config.groupByColumn !== run.groupByColumn ||
    config.measureColumn !== run.measureColumn ||
    config.aggregation !== run.aggregation;
  const title = config.title.trim() || queryCaption(config);
  const update = (value: WidgetInput) => {
    setConfig(value);
    try {
      saveExploration(scope, value);
      setStorageError("");
    } catch {
      setStorageError(
        "Browser storage is unavailable. Keep this page open to preserve your draft.",
      );
    }
  };
  return (
    <>
      <div className="explore-layout">
        <aside className="explore-controls">
          <div className="analytics-section-label">
            <span className="step-number">1</span> Choose your data
          </div>
          <Select
            aria-label="Dataset"
            data={datasets
              .filter((item) => item.currentVersion)
              .map((item) => ({ value: item.id, label: item.name }))}
            value={dataset.id}
            onChange={(value) => value && onDataset(value)}
            allowDeselect={false}
          />
          <div className="dataset-source-note">
            <Icon name="data" size={14} />
            {profile.rowCount.toLocaleString()} rows · {profile.columnCount}{" "}
            columns
          </div>
          <div className="analytics-section-label">
            <span className="step-number">2</span> Ask a question
          </div>
          <QueryFields
            profile={profile}
            config={config}
            onChange={(value) => update({ ...config, ...value })}
          />
          <Button
            fullWidth
            leftSection={<Icon name="play" size={15} />}
            loading={results.isFetching}
            disabled={!config.measureColumn || !config.groupByColumn}
            onClick={() => {
              const next = {
                groupByColumn: config.groupByColumn,
                measureColumn: config.measureColumn,
                aggregation: config.aggregation,
              };
              if (!changed) void results.refetch();
              else setRun(next);
            }}
          >
            Run query
          </Button>
          <p className="analytics-note">
            Your query uses the current dataset version. No code needed.
          </p>
        </aside>
        <section className="explore-result">
          <div className="explore-result-toolbar">
            <div>
              <span className="eyebrow">VISUALIZATION</span>
              <h2>{title}</h2>
            </div>
            <SegmentedControl
              size="xs"
              value={view}
              onChange={setView}
              data={[
                { value: "chart", label: "Chart" },
                { value: "table", label: "Table" },
              ]}
            />
          </div>
          <div className="explore-chart-area">
            {changed && (
              <div className="query-stale">
                <Icon name="clock" size={15} />
                Your question has changed. Run the query to update these
                results.
              </div>
            )}
            {results.isPending ? (
              <LoadingState label="Running your query…" />
            ) : results.isError ? (
              <ErrorState
                error={results.error}
                retry={() => results.refetch()}
              />
            ) : results.data.length === 0 ? (
              <EmptyState
                icon="search"
                title="No matching values"
                description="Try another measure or aggregation."
              />
            ) : view === "table" ? (
              <ResultsTable items={results.data} config={run} />
            ) : (
              <Chart
                items={results.data}
                type={config.type}
                title={title}
                aggregation={run.aggregation}
              />
            )}
          </div>
          <div className="explore-result-footer">
            <span>
              <i className="status-dot" />
              {results.data?.length || 0} groups{" "}
              <span className="analytics-dot">·</span> {dataset.name}
            </span>
            <span>Source: current version</span>
          </div>
          <div className="explore-style">
            <div>
              <div className="analytics-section-label">
                <span className="step-number">3</span> Make it clear
              </div>
              <ChartTypePicker
                value={config.type}
                onChange={(type) => update({ ...config, type })}
              />
            </div>
            <div className="chart-title-field">
              <TextInput
                label="Chart title"
                placeholder={queryCaption(config)}
                value={config.title}
                maxLength={200}
                onChange={(event) =>
                  update({ ...config, title: event.currentTarget.value })
                }
              />
              <p className="analytics-note">
                A clear title makes the takeaway easier to find.
              </p>
            </div>
          </div>
        </section>
      </div>
      {storageError && (
        <p role="alert" className="analytics-storage-error">
          {storageError}
        </p>
      )}
      <div className="explore-next">
        <div>
          <Icon name="dashboard" size={21} />
          <div>
            <strong>Keep the insight. Build the bigger picture.</strong>
            <p>Add this chart to a dashboard alongside your other findings.</p>
          </div>
        </div>
        <Button
          rightSection={<Icon name="arrow" size={16} />}
          disabled={
            changed ||
            !results.data?.length ||
            results.isFetching ||
            Boolean(results.error)
          }
          onClick={() => setSaving(true)}
        >
          Add to dashboard
        </Button>
      </div>
      <SaveChartDialog
        opened={saving}
        onClose={() => setSaving(false)}
        projectId={projectId}
        input={{ ...config, title }}
      />
    </>
  );
}

function SaveChartDialog({
  opened,
  onClose,
  projectId,
  input,
}: {
  opened: boolean;
  onClose: () => void;
  projectId: string;
  input: WidgetInput;
}) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [target, setTarget] = useState<string | null>(null);
  const [name, setName] = useState("");
  const dashboards = useDashboards(projectId, opened);
  const save = useMutation({
    mutationFn: async () => {
      let dashboardId = target;
      if (target === "__new__") {
        const created = await adapter.createDashboard(projectId, name.trim());
        dashboardId = created.id;
        setTarget(created.id);
        await queryClient.invalidateQueries({
          queryKey: queryKeys.dashboards.list(projectId),
        });
      }
      if (!dashboardId) throw new Error("Choose a dashboard first.");
      await adapter.createWidget(projectId, dashboardId, input);
      return dashboardId;
    },
    onSuccess: async (dashboardId) => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
      });
      onClose();
      navigate(`/projects/${projectId}/dashboards/${dashboardId}/edit`);
    },
  });
  return (
    <Modal
      opened={opened}
      onClose={save.isPending ? () => {} : onClose}
      title="Give your insight a home"
      centered
    >
      <div className="analytics-dialog">
        <p className="muted">
          Add <strong>{input.title}</strong> to an existing dashboard, or start
          a fresh canvas.
        </p>
        {dashboards.isError ? (
          <ErrorState
            error={dashboards.error}
            retry={() => dashboards.refetch()}
          />
        ) : (
          <Select
            label="Dashboard"
            placeholder={
              dashboards.isPending
                ? "Loading dashboards…"
                : "Choose a dashboard"
            }
            data={[
              ...(dashboards.data || []).map((item) => ({
                value: item.id,
                label: item.name,
              })),
              { value: "__new__", label: "+ Create a new dashboard" },
            ]}
            value={target}
            onChange={setTarget}
            disabled={dashboards.isPending || save.isPending}
          />
        )}{" "}
        {target === "__new__" && (
          <TextInput
            label="Dashboard name"
            placeholder="e.g. Commercial overview"
            maxLength={100}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
          />
        )}{" "}
        {save.isError && <ErrorState error={save.error} />}
        <div className="analytics-dialog-actions">
          <Button variant="default" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button
            loading={save.isPending}
            disabled={!target || (target === "__new__" && !name.trim())}
            onClick={() => save.mutate()}
          >
            Add chart
          </Button>
        </div>
      </div>
    </Modal>
  );
}

function WidgetChart({
  widget,
  projectId,
  compact = false,
}: {
  widget: Widget;
  projectId: string;
  compact?: boolean;
}) {
  const { adapter } = useSession();
  const config = {
    groupByColumn: widget.groupByColumn,
    measureColumn: widget.measureColumn,
    aggregation: widget.aggregation,
  };
  const data = useQuery({
    queryKey: queryKeys.analytics.chartData(
        projectId,
        widget.datasetId,
        config,
    ),
    queryFn: () => adapter.query(projectId, widget.datasetId, config),
  });
  if (data.isPending)
    return (
      <div className="widget-loading" role="status">
        Loading chart…
      </div>
    );
  if (data.isError)
    return compact ? (
      <div className="widget-loading">Chart unavailable</div>
    ) : (
      <ErrorState error={data.error} retry={() => data.refetch()} />
    );
  return (
    <Chart
      type={widget.type}
      items={data.data}
      title={widget.title}
      compact={compact}
      aggregation={widget.aggregation}
    />
  );
}

function DashboardThumbnail({
  projectId,
  dashboardId,
}: {
  projectId: string;
  dashboardId: string;
}) {
  const { mode, session } = useSession();
  const widgets = useDashboardWidgets(projectId, dashboardId);
  const saved = readEditor(
    editorScope(mode, session?.user.id || "", projectId),
    dashboardId,
    false,
  ).document;
  const ordered = orderedWidgets(widgets.data || [], saved);
  return (
    <div className="dashboard-thumbnail" aria-hidden="true">
      <div className="thumbnail-heading">
        <b />
        <span />
      </div>
      {ordered.length ? (
        <div className="thumbnail-charts">
          {ordered.slice(0, 2).map((widget) => (
            <div key={widget.id}>
              <span className="thumbnail-chart-title">{widget.title}</span>
              <WidgetChart widget={widget} projectId={projectId} compact />
            </div>
          ))}
        </div>
      ) : (
        <div className="thumbnail-empty">
          <Icon name="dashboard" size={30} />
          <span>
            {widgets.isPending
              ? "Loading canvas…"
              : "Your next story starts here"}
          </span>
        </div>
      )}
    </div>
  );
}

export function DashboardsPage() {
  const { projectId = "" } = useParams();
  const { mode, session } = useSession();
  const navigate = useNavigate();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [search, setSearch] = useState("");
  const [deleting, setDeleting] = useState<Dashboard | null>(null);
  const dashboards = useDashboards(projectId);
  const create = useCreateDashboard(projectId);
  const remove = useDeleteDashboard(projectId);
  const filtered =
    dashboards.data?.filter((item) =>
      item.name.toLowerCase().includes(search.toLowerCase()),
    ) || [];
  return (
    <div className="page-content analytics-page">
      <PageHeading
        eyebrow="THE BIGGER PICTURE"
        title="Your data, brought together."
        description="A home for the questions that matter. Build a clear picture, one insight at a time."
        actions={
          <Button
            leftSection={<Icon name="plus" size={17} />}
            onClick={() => {
              setName("");
              create.reset();
              setCreateOpen(true);
            }}
          >
            New dashboard
          </Button>
        }
      />
      <div className="dashboard-list-toolbar">
        <strong>
          All dashboards <span>{dashboards.data?.length || 0}</span>
        </strong>
        <TextInput
          aria-label="Search dashboards"
          placeholder="Find a dashboard…"
          leftSection={<Icon name="search" size={16} />}
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
        />
      </div>
      {dashboards.isPending ? (
        <LoadingState />
      ) : dashboards.isError ? (
        <ErrorState
          error={dashboards.error}
          retry={() => dashboards.refetch()}
        />
      ) : !dashboards.data.length ? (
        <EmptyState
          icon="dashboard"
          title="A blank canvas. A clearer picture."
          description="Bring your charts together in a dashboard you can come back to."
          action={
            <Button
              onClick={() => setCreateOpen(true)}
              leftSection={<Icon name="plus" />}
            >
              Create your first dashboard
            </Button>
          }
        />
      ) : !filtered.length ? (
        <EmptyState
          icon="search"
          title="No dashboards found"
          description="Try a different name or clear your search."
          action={
            <Button variant="default" onClick={() => setSearch("")}>
              Clear search
            </Button>
          }
        />
      ) : (
        <div className="dashboard-card-grid">
          {filtered.map((dashboard) => (
            <article className="dashboard-card" key={dashboard.id}>
              <Link
                to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                className="dashboard-card-link"
              >
                <DashboardThumbnail
                  projectId={projectId}
                  dashboardId={dashboard.id}
                />
                <div className="dashboard-card-info">
                  <span className="dashboard-card-icon">
                    <Icon name="dashboard" />
                  </span>
                  <div>
                    <h2>{dashboard.name}</h2>
                    <p>
                      Created{" "}
                      {new Date(dashboard.createdAtUtc).toLocaleDateString(
                        "en",
                        { month: "short", day: "numeric", year: "numeric" },
                      )}
                    </p>
                  </div>
                  <Icon name="arrow" size={17} />
                </div>
              </Link>
              <Menu position="bottom-end">
                <Menu.Target>
                  <ActionIcon
                    variant="subtle"
                    className="dashboard-card-menu"
                    aria-label={`Options for ${dashboard.name}`}
                  >
                    <Icon name="more" />
                  </ActionIcon>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item
                    component={Link}
                    to={`/projects/${projectId}/dashboards/${dashboard.id}/view`}
                    leftSection={<Icon name="eye" size={16} />}
                  >
                    View dashboard
                  </Menu.Item>
                  <Menu.Item
                    component={Link}
                    to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                    leftSection={<Icon name="edit" size={16} />}
                  >
                    Edit dashboard
                  </Menu.Item>
                  <Menu.Divider />
                  <Menu.Item
                    color="red"
                    leftSection={<Icon name="trash" size={16} />}
                    onClick={() => {
                      remove.reset();
                      setDeleting(dashboard);
                    }}
                  >
                    Delete dashboard
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            </article>
          ))}
          <button
            className="dashboard-create-card"
            onClick={() => setCreateOpen(true)}
          >
            <span>
              <Icon name="plus" size={24} />
            </span>
            <strong>Start with a blank canvas</strong>
            <small>Make room for a new perspective</small>
          </button>
        </div>
      )}
      <Modal
        opened={createOpen}
        onClose={create.isPending ? () => {} : () => setCreateOpen(false)}
        title="Create a dashboard"
        centered
      >
        <form
          className="analytics-dialog"
          onSubmit={(event) => {
            event.preventDefault();
            if (name.trim()) create.mutate(name.trim(), {
              onSuccess: (dashboard) => {
                navigate(`/projects/${projectId}/dashboards/${dashboard.id}/edit`);
              },
            });
          }}
        >
          <p className="muted">
            Give your dashboard a purpose. You can add charts from any dataset
            in this project.
          </p>
          <TextInput
            label="Dashboard name"
            placeholder="e.g. Commercial overview"
            required
            maxLength={100}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
            data-autofocus
          />
          {create.isError && <ErrorState error={create.error} />}
          <div className="analytics-dialog-actions">
            <Button
              variant="default"
              onClick={() => setCreateOpen(false)}
              disabled={create.isPending}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={!name.trim()}
              loading={create.isPending}
            >
              Create dashboard
            </Button>
          </div>
        </form>
      </Modal>
      <Modal
        opened={Boolean(deleting)}
        onClose={remove.isPending ? () => {} : () => setDeleting(null)}
        title="Delete this dashboard?"
        centered
      >
        <div className="analytics-dialog">
          <p>
            <strong>{deleting?.name}</strong> and its saved charts will be
            permanently deleted. Your datasets will remain available.
          </p>
          {remove.isError && <ErrorState error={remove.error} />}
          <div className="analytics-dialog-actions">
            <Button
              variant="default"
              onClick={() => setDeleting(null)}
              disabled={remove.isPending}
            >
              Keep dashboard
            </Button>
            <Button
                color="red"
                loading={remove.isPending}
                onClick={() => {
                  if (!deleting) return;

                  const dashboard = deleting;

                  remove.mutate(dashboard.id, {
                    onSuccess: () => {
                      try {
                        removeEditor(
                            editorScope(mode, session?.user.id || "", projectId),
                            dashboard.id,
                        );
                      } catch {}

                      setDeleting(null);
                    },
                  });
                }}
            >
              Delete dashboard
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

function orderedWidgets(widgets: Widget[], document: EditorDocument) {
  const ids = [
    ...document.order.filter((id) =>
      widgets.some((widget) => widget.id === id),
    ),
    ...widgets
      .filter((widget) => !document.order.includes(widget.id))
      .map((widget) => widget.id),
  ];
  return ids.map((id) => ({
    ...widgets.find((widget) => widget.id === id)!,
    ...document.overrides[id],
  }));
}

export function DashboardEditorPage() {
  return <DashboardLoader editing />;
}
export function DashboardViewPage() {
  return <DashboardLoader editing={false} />;
}
function DashboardLoader({ editing }: { editing: boolean }) {
  const { projectId = "", dashboardId = "" } = useParams();
  const dashboard = useDashboard(projectId, dashboardId);
  const widgets = useDashboardWidgets(projectId, dashboardId);
  if (dashboard.isPending || widgets.isPending)
    return (
      <main className="canvas-loading">
        <Brand />
        <LoadingState label="Opening your dashboard…" />
      </main>
    );
  if (dashboard.isError || widgets.isError)
    return (
      <main className="canvas-loading">
        <Brand />
        <Link className="text-link" to={`/projects/${projectId}/dashboards`}>
          Back to dashboards
        </Link>
        <ErrorState
          error={dashboard.error || widgets.error}
          retry={() => {
            void dashboard.refetch();
            void widgets.refetch();
          }}
        />
      </main>
    );
  return (
    <DashboardWorkspace
      key={`${dashboardId}:${editing}`}
      dashboard={dashboard.data}
      sourceWidgets={widgets.data}
      editing={editing}
    />
  );
}

function DashboardWorkspace({
  dashboard,
  sourceWidgets,
  editing,
}: {
  dashboard: Dashboard;
  sourceWidgets: Widget[];
  editing: boolean;
}) {
  const { adapter, mode, session } = useSession();
  const navigate = useNavigate();
  const scope = editorScope(mode, session?.user.id || "", dashboard.projectId);
  const [initial] = useState(() => readEditor(scope, dashboard.id, editing));
  const [document, setDocument] = useState(initial.document);
  const [dirty, setDirty] = useState(initial.dirty);
  const [selectedId, setSelectedId] = useState(sourceWidgets[0]?.id || "");
  const [addOpen, setAddOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [saveMessage, setSaveMessage] = useState("");
  const [showTable, setShowTable] = useState(false);
  const widgets = orderedWidgets(sourceWidgets, document);
  const selected =
    widgets.find((widget) => widget.id === selectedId) || widgets[0];
  const datasets = useQuery({
    queryKey: queryKeys.datasets.list(dashboard.projectId),
    queryFn: () => adapter.listDatasets(dashboard.projectId),
    enabled: editing,
  });
  useEffect(() => {
    if (!dirty || !editing) return;
    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, [dirty, editing]);
  function update(next: EditorDocument) {
    setDocument(next);
    setDirty(true);
    setSaveMessage("");
    try {
      persistEditorDraft(scope, dashboard.id, next);
      setSaveError("");
    } catch {
      setSaveError(
        "Your browser could not save this draft. Free some browser storage before leaving.",
      );
    }
  }
  function persist() {
    if (
      widgets.some(
        (widget) =>
          !widget.title.trim() ||
          !widget.groupByColumn ||
          !widget.measureColumn,
      )
    ) {
      setSaveError(
        "Give every chart a title and choose its group and measure before saving.",
      );
      return false;
    }
    try {
      const saved = saveEditor(scope, dashboard.id, document);
      setDocument(saved);
      setDirty(false);
      setSaveError("");
      setSaveMessage("Saved in this browser");
      return true;
    } catch {
      setSaveError(
        "Your browser could not save these edits. Free some browser storage and try again.",
      );
      return false;
    }
  }
  function leave(path: string) {
    if (
      dirty &&
      !window.confirm(
        "Leave with unsaved layout changes? Your draft will stay in this browser, but the presentation shows your last saved layout.",
      )
    )
      return;
    navigate(path);
  }
  function changeWidget(patch: Partial<WidgetInput>) {
    if (selected)
      update({
        ...document,
        overrides: {
          ...document.overrides,
          [selected.id]: { ...document.overrides[selected.id], ...patch },
        },
      });
  }
  function move(direction: -1 | 1) {
    if (!selected) return;
    const ids = widgets.map((widget) => widget.id);
    const index = ids.indexOf(selected.id);
    if (index + direction < 0 || index + direction >= ids.length) return;
    [ids[index], ids[index + direction]] = [ids[index + direction], ids[index]];
    update({ ...document, order: ids });
  }
  const remove = useDeleteWidget(dashboard.projectId);
  return (
    <div
      className={`dashboard-workspace ${editing ? "is-editing" : "is-viewing"}`}
    >
      <header className="canvas-header">
        <div
          className="canvas-header-start"
          onClickCapture={(event) => {
            if (
              dirty &&
              (event.target as HTMLElement).closest("a") &&
              !window.confirm(
                "Leave this editor? Your unsaved draft will remain in this browser.",
              )
            )
              event.preventDefault();
          }}
        >
          <Brand />
          <span className="canvas-header-divider" />
          <button
            className="canvas-back"
            onClick={() => leave(`/projects/${dashboard.projectId}/dashboards`)}
          >
            <Icon name="back" size={16} />
            <span>Dashboards</span>
          </button>
        </div>
        <div className="canvas-document-name">
          <Icon name="dashboard" size={17} />
          <strong>{dashboard.name}</strong>
          <span className="canvas-mode-label">
            {editing ? "Editor" : "Presentation"}
          </span>
        </div>
        <div className="canvas-actions">
          {mode === "demo" && <span className="canvas-demo-label">Demo</span>}
          {editing ? (
            <>
              <Tooltip label="View your saved dashboard">
                <Button
                  variant="default"
                  size="sm"
                  leftSection={<Icon name="eye" size={16} />}
                  onClick={() => {
                    if (!dirty || persist())
                      navigate(
                        `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/view`,
                      );
                  }}
                >
                  Preview
                </Button>
              </Tooltip>
              <Button
                size="sm"
                leftSection={<Icon name="check" size={16} />}
                onClick={persist}
              >
                Save changes
              </Button>
            </>
          ) : (
            <Button
              variant="default"
              leftSection={<Icon name="edit" size={16} />}
              onClick={() =>
                navigate(
                  `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/edit`,
                )
              }
            >
              Edit dashboard
            </Button>
          )}
        </div>
      </header>
      {editing && (
        <div className="canvas-toolbar">
          <div>
            <span
              className={dirty ? "save-indicator unsaved" : "save-indicator"}
            />
            <span role="status">
              {dirty
                ? "Unsaved changes · draft kept locally"
                : saveMessage || "Layout and edits saved in this browser"}
            </span>
          </div>
          <Button
            size="xs"
            variant="subtle"
            leftSection={<Icon name="plus" size={15} />}
            onClick={() => setAddOpen(true)}
          >
            Add chart
          </Button>
        </div>
      )}
      {saveError && (
        <div className="analytics-storage-error" role="alert">
          {saveError}
        </div>
      )}
      <div className="canvas-work-area">
        {editing && (
          <aside className="canvas-outline">
            <div className="canvas-panel-heading">
              Outline <span>{widgets.length}</span>
            </div>
            <p className="canvas-panel-hint">Your dashboard at a glance</p>
            <div className="outline-items">
              {widgets.map((widget, index) => (
                <button
                  key={widget.id}
                  className={selected?.id === widget.id ? "selected" : ""}
                  onClick={() => {
                    setSelectedId(widget.id);
                    setShowTable(false);
                  }}
                  aria-pressed={selected?.id === widget.id}
                >
                  <Icon
                    name={
                      chartTypes.find((type) => type.value === widget.type)
                        ?.icon || "chart"
                    }
                    size={16}
                  />
                  <span>{widget.title}</span>
                  <small>{index + 1}</small>
                </button>
              ))}
            </div>
            <button className="outline-add" onClick={() => setAddOpen(true)}>
              <Icon name="plus" size={16} />
              Add a chart
            </button>
            <div className="canvas-outline-bottom">
              <Icon name="help" size={17} />
              <p>Select a chart to adjust its question, style and position.</p>
            </div>
          </aside>
        )}
        <main className="canvas-stage">
          <div className="canvas-size-label">
            {editing ? "DASHBOARD CANVAS" : "PROJECT DASHBOARD"}
            <span>
              {widgets.length} {widgets.length === 1 ? "chart" : "charts"}
            </span>
          </div>
          <div className="dashboard-paper">
            <div className="dashboard-paper-heading">
              <span className="eyebrow">AN OVERVIEW THAT MATTERS</span>
              <h1>{dashboard.name}</h1>
              <p>
                {editing
                  ? "Bring your findings into focus."
                  : "A clear view of your latest data."}
              </p>
              <div className="dashboard-paper-meta">
                <span>
                  <i className="status-dot" />
                  Current dataset versions
                </span>
                <span>
                  {mode === "demo" ? "Demo workspace" : "Personal workspace"}
                </span>
              </div>
            </div>
            {widgets.length ? (
              <div className="canvas-chart-grid">
                {widgets.map((widget) => (
                  <section
                    key={widget.id}
                    className={`canvas-chart ${document.widths[widget.id] === "full" ? "full-width" : ""} ${editing && selected?.id === widget.id ? "is-selected" : ""} ${widget.type === "Kpi" ? "is-kpi" : ""}`}
                    onClick={(event) => {
                      if (
                        editing &&
                        !(event.target as Element).closest(
                          "button, a, input, select",
                        )
                      ) {
                        setSelectedId(widget.id);
                        setShowTable(false);
                      }
                    }}
                  >
                    <div className="canvas-chart-heading">
                      <div>
                        <h2>{widget.title}</h2>
                        <p>{queryCaption(widget)}</p>
                      </div>
                      {editing && (
                        <Tooltip label={`Configure ${widget.title}`}>
                          <ActionIcon
                            variant={
                              selected?.id === widget.id ? "light" : "subtle"
                            }
                            aria-label={`Configure ${widget.title}`}
                            onClick={() => {
                              setSelectedId(widget.id);
                              setShowTable(false);
                            }}
                          >
                            <Icon name="settings" size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                      {!editing && (
                        <Tooltip label="Inspect results">
                          <ActionIcon
                            variant="subtle"
                            aria-label={`Inspect results for ${widget.title}`}
                            onClick={() => {
                              setSelectedId(widget.id);
                              setShowTable(true);
                            }}
                          >
                            <Icon name="data" size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                    </div>
                    <WidgetChart
                      widget={widget}
                      projectId={dashboard.projectId}
                    />
                  </section>
                ))}
              </div>
            ) : (
              <EmptyState
                icon="chart"
                title="What would you like to understand?"
                description="Add a chart from your project data. Build a story by bringing your findings together."
                action={
                  editing && (
                    <Button
                      leftSection={<Icon name="plus" />}
                      onClick={() => setAddOpen(true)}
                    >
                      Add your first chart
                    </Button>
                  )
                }
              />
            )}
            <footer className="dashboard-paper-footer">
              <span>Aperture</span>
              <span>
                {editing
                  ? "Designed for a clearer perspective"
                  : "Your data. A clearer perspective."}
              </span>
            </footer>
          </div>
          {editing && (
            <p className="canvas-stage-hint">
              <Icon name="shield" size={14} />
              New charts are saved to your{" "}
              {mode === "demo" ? "demo workspace" : "account"}. Layout and chart
              edits stay in this browser.
            </p>
          )}
        </main>
        {editing && (
          <aside className="canvas-inspector">
            <div className="canvas-panel-heading">
              Chart properties <Icon name="settings" size={16} />
            </div>
            {selected ? (
              <>
                <div className="inspector-section">
                  <TextInput
                    label="Title"
                    value={selected.title}
                    maxLength={200}
                    onChange={(event) =>
                      changeWidget({ title: event.currentTarget.value })
                    }
                  />
                  <span className="inspector-label">Visualization</span>
                  <ChartTypePicker
                    value={selected.type}
                    onChange={(type) => changeWidget({ type })}
                  />
                </div>
                <div className="inspector-section">
                  <span className="inspector-label">Layout</span>
                  <SegmentedControl
                    fullWidth
                    size="xs"
                    value={document.widths[selected.id] || "half"}
                    onChange={(value) =>
                      update({
                        ...document,
                        widths: {
                          ...document.widths,
                          [selected.id]: value as "half" | "full",
                        },
                      })
                    }
                    data={[
                      { value: "half", label: "Half width" },
                      { value: "full", label: "Full width" },
                    ]}
                  />
                  <div className="inspector-reorder">
                    <span>Position</span>
                    <Button
                      variant="default"
                      size="compact-xs"
                      disabled={widgets[0].id === selected.id}
                      onClick={() => move(-1)}
                    >
                      Move earlier
                    </Button>
                    <Button
                      variant="default"
                      size="compact-xs"
                      disabled={widgets[widgets.length - 1].id === selected.id}
                      onClick={() => move(1)}
                    >
                      Move later
                    </Button>
                  </div>
                </div>
                <InspectorQuery
                  key={selected.id}
                  widget={selected}
                  projectId={dashboard.projectId}
                  onChange={changeWidget}
                  onOpenDataset={() =>
                    leave(
                      `/projects/${dashboard.projectId}/data/${selected.datasetId}`,
                    )
                  }
                />
                <div className="inspector-section">
                  <Button
                    fullWidth
                    variant="default"
                    size="xs"
                    leftSection={<Icon name="data" size={15} />}
                    onClick={() => setShowTable(true)}
                  >
                    Inspect results
                  </Button>
                  <Button
                    fullWidth
                    variant="subtle"
                    color="red"
                    size="xs"
                    mt={8}
                    leftSection={<Icon name="trash" size={15} />}
                    onClick={() => {
                      remove.reset();
                      setDeleteOpen(true);
                    }}
                  >
                    Remove chart
                  </Button>
                </div>
              </>
            ) : (
              <div className="inspector-empty">
                <Icon name="settings" size={28} />
                <h3>A little room for possibility</h3>
                <p>Add a chart, then make it your own here.</p>
              </div>
            )}
          </aside>
        )}
      </div>
      <NewChartDialog
        opened={addOpen}
        onClose={() => setAddOpen(false)}
        dashboard={dashboard}
        datasets={datasets.data || []}
        datasetsLoading={datasets.isPending}
        datasetsError={datasets.error}
        retryDatasets={() => datasets.refetch()}
        onCreated={(id) => {
          setSelectedId(id);
          setAddOpen(false);
        }}
      />
      <Modal
        opened={deleteOpen}
        onClose={remove.isPending ? () => {} : () => setDeleteOpen(false)}
        title="Remove this chart?"
        centered
      >
        <div className="analytics-dialog">
          <p>
            <strong>{selected?.title}</strong> will be permanently removed from
            this dashboard. Your source dataset will stay available.
          </p>
          {remove.isError && <ErrorState error={remove.error} />}
          <div className="analytics-dialog-actions">
            <Button
              variant="default"
              onClick={() => setDeleteOpen(false)}
              disabled={remove.isPending}
            >
              Keep chart
            </Button>
            <Button
                color="red"
                loading={remove.isPending}
                onClick={() => {
                  if (!selected) return;

                  remove.mutate(
                      {
                        dashboardId: dashboard.id,
                        widgetId: selected.id,
                      },
                      {
                        onSuccess: () => setDeleteOpen(false),
                      },
                  );
                }}
            >
              Remove chart
            </Button>
          </div>
        </div>
      </Modal>
      <Modal
        opened={showTable && Boolean(selected)}
        onClose={() => setShowTable(false)}
        title={selected?.title}
        size="lg"
        centered
      >
        {selected && (
          <WidgetResults widget={selected} projectId={dashboard.projectId} />
        )}
      </Modal>
    </div>
  );
}

function InspectorQuery({
  widget,
  projectId,
  onChange,
  onOpenDataset,
}: {
  widget: Widget;
  projectId: string;
  onChange: (value: Partial<WidgetInput>) => void;
  onOpenDataset: () => void;
}) {
  const { adapter } = useSession();
  const profile = useQuery({
    queryKey: queryKeys.datasets.profile(
        projectId,
        widget.datasetId,
    ),
    queryFn: () => adapter.getProfile(projectId, widget.datasetId),
  });
  return (
    <div className="inspector-section">
      <span className="inspector-label">Data & question</span>
      {profile.isPending ? (
        <p className="muted">Reading columns…</p>
      ) : profile.isError ? (
        <ErrorState error={profile.error} retry={() => profile.refetch()} />
      ) : (
        <>
          <button className="inspector-dataset" onClick={onOpenDataset}>
            <Icon name="data" size={15} />
            {profile.data.name}
            <Icon name="chevron" size={13} />
          </button>
          <QueryFields
            profile={profile.data}
            config={widget}
            onChange={onChange}
          />
        </>
      )}
      <p className="analytics-note">
        Changes update the chart preview. Save to keep this configuration in
        this browser.
      </p>
    </div>
  );
}
function WidgetResults({
  widget,
  projectId,
}: {
  widget: Widget;
  projectId: string;
}) {
  const { adapter } = useSession();
  const config = {
    groupByColumn: widget.groupByColumn,
    measureColumn: widget.measureColumn,
    aggregation: widget.aggregation,
  };
  const results = useQuery({
    queryKey: queryKeys.analytics.chartData(
        projectId,
        widget.datasetId,
        config,
    ),
    queryFn: () => adapter.query(projectId, widget.datasetId, config),
  });
  return results.isPending ? (
    <LoadingState />
  ) : results.isError ? (
    <ErrorState error={results.error} />
  ) : (
    <ResultsTable items={results.data} config={config} />
  );
}

function NewChartDialog({
  opened,
  onClose,
  dashboard,
  datasets,
  datasetsLoading,
  datasetsError,
  retryDatasets,
  onCreated,
}: {
  opened: boolean;
  onClose: () => void;
  dashboard: Dashboard;
  datasets: Dataset[];
  datasetsLoading: boolean;
  datasetsError: unknown;
  retryDatasets: () => void;
  onCreated: (id: string) => void;
}) {
  const { adapter } = useSession();
  const [datasetId, setDatasetId] = useState("");
  const [busy, setBusy] = useState(false);
  const current =
    datasets.find((item) => item.id === datasetId) ||
    datasets.find((item) => item.currentVersion);
  const profile = useQuery({
    queryKey: queryKeys.datasets.profile(
        dashboard.projectId,
        current?.id,
    ),
    queryFn: () => adapter.getProfile(dashboard.projectId, current!.id),
    enabled: opened && Boolean(current),
  });
  return (
    <Modal
      opened={opened}
      onClose={busy ? () => {} : onClose}
      title="Add a chart"
      size="lg"
      centered
    >
      <div className="analytics-dialog">
        <p className="muted">Turn a question into something you can see.</p>
        {datasetsLoading ? (
          <LoadingState />
        ) : datasetsError ? (
          <ErrorState error={datasetsError} retry={retryDatasets} />
        ) : !current ? (
          <EmptyState
            icon="data"
            title="First, add some data"
            description="Charts need a dataset to draw from. Import a CSV into this project to get started."
            action={
              <Button
                component={Link}
                to={`/projects/${dashboard.projectId}/data`}
              >
                Go to project data
              </Button>
            }
          />
        ) : (
          <>
            <Select
              label="Source dataset"
              disabled={busy}
              data={datasets
                .filter((item) => item.currentVersion)
                .map((item) => ({ value: item.id, label: item.name }))}
              value={current.id}
              onChange={(value) => value && setDatasetId(value)}
              allowDeselect={false}
            />
            {profile.isPending ? (
              <LoadingState />
            ) : profile.isError ? (
              <ErrorState
                error={profile.error}
                retry={() => profile.refetch()}
              />
            ) : (
              <NewChartForm
                key={`${current.id}:${opened}`}
                dashboard={dashboard}
                dataset={current}
                profile={profile.data}
                onClose={onClose}
                onCreated={onCreated}
                onBusyChange={setBusy}
              />
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
function NewChartForm({
  dashboard,
  dataset,
  profile,
  onClose,
  onCreated,
  onBusyChange,
}: {
  dashboard: Dashboard;
  dataset: Dataset;
  profile: DatasetProfile;
  onClose: () => void;
  onCreated: (id: string) => void;
  onBusyChange: (busy: boolean) => void;
}) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();
  const [input, setInput] = useState<WidgetInput>({
    datasetId: dataset.id,
    ...defaults(profile),
    type: "BarChart",
    title: "",
  });
  const create = useMutation({
    mutationFn: async () => {
      const data = await adapter.query(dashboard.projectId, dataset.id, input);
      if (!data.length)
        throw new Error(
          "This question returned no values. Try another measure.",
        );
      return adapter.createWidget(dashboard.projectId, dashboard.id, {
        ...input,
        title: input.title.trim() || queryCaption(input),
      });
    },
    onSuccess: async (widget) => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.widgets(
            dashboard.projectId,
            dashboard.id,
        )
      });
      onCreated(widget.id);
    },
    onSettled: () => onBusyChange(false),
  });
  return (
    <form
      className="analytics-dialog"
      onSubmit={(event) => {
        event.preventDefault();
        onBusyChange(true);
        create.mutate();
      }}
    >
      <ChartTypePicker
        value={input.type}
        onChange={(type) => setInput({ ...input, type })}
      />
      <div className="new-chart-fields">
        <QueryFields
          profile={profile}
          config={input}
          onChange={(value) => setInput({ ...input, ...value })}
        />
      </div>
      <TextInput
        label="Chart title"
        placeholder={queryCaption(input)}
        value={input.title}
        maxLength={200}
        onChange={(event) =>
          setInput({ ...input, title: event.currentTarget.value })
        }
      />
      <div className="query-sentence">
        <Icon name="chart" size={18} />
        <span>{queryCaption(input)}</span>
      </div>
      {create.isError && <ErrorState error={create.error} />}
      <div className="analytics-dialog-actions">
        <Button variant="default" disabled={create.isPending} onClick={onClose}>
          Cancel
        </Button>
        <Button
          type="submit"
          loading={create.isPending}
          disabled={!input.measureColumn || !input.groupByColumn}
        >
          Add to canvas
        </Button>
      </div>
    </form>
  );
}
