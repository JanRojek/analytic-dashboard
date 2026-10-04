import { useState, type FormEvent } from "react";

import {
  useCreateProject,
  useDeleteProject,
  useProjects,
  useRenameProject,
} from "../hooks";

import {
  useDatasets,
  useDatasetProfile,
  useDatasetUsage,
  useDeleteDataset,
} from "../../data/hooks";

import {
  useDashboards,
  useDeleteDashboard,
} from "../../dashboards/hooks";

import { Link, useNavigate, useParams } from "react-router-dom";

import {
  ActionIcon,
  Alert,
  Button,
  Menu,
  Modal,
  Select,
  Textarea,
  TextInput,
  Tooltip,
} from "@mantine/core";

import type { Dashboard, Dataset, Project } from "../../../data/types";

import { Icon } from "../../../components/Icon";
import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";

import { ImportDataDialog } from "../../data/components/ImportDataDialog";

import "../styles/projects.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

function ProjectDialog({
                         opened,
                         onClose,
                         project,
                       }: {
  opened: boolean;
  onClose: () => void;
  project?: Project;
}) {
  const navigate = useNavigate();
  const [name, setName] = useState(project?.name || "");
  const [description, setDescription] = useState("");

  const createProject = useCreateProject();
  const renameProject = useRenameProject();

  const isPending = createProject.isPending || renameProject.isPending;
  const error = project ? renameProject.error : createProject.error;

  function submit(e: FormEvent) {
    e.preventDefault();

    const trimmedName = name.trim();

    if (!trimmedName || isPending) return;

    if (project) {
      renameProject.mutate(
          {
            projectId: project.id,
            name: trimmedName,
          },
          {
            onSuccess: () => {
              onClose();
            },
          },
      );

      return;
    }

    createProject.mutate(
        {
          name: trimmedName,
          description: description.trim(),
        },
        {
          onSuccess: (createdProject) => {
            onClose();
            navigate(`/projects/${createdProject.id}`);
          },
        },
    );
  }

  return (
      <Modal
          opened={opened}
          onClose={() => {
            if (!isPending) onClose();
          }}
          closeOnClickOutside={!isPending}
          closeOnEscape={!isPending}
          withCloseButton={!isPending}
          title={project ? "Rename project" : "Make room for a new question"}
          centered
          size="md"
      >
        <form onSubmit={submit}>
          <div className="new-project-intro">
            <p className="muted">
              {project
                  ? "Give this project a name that makes it easy to recognize."
                  : "Start with a name. You can add your first data source once the project is ready."}
            </p>
          </div>

          <TextInput
              label="Project name"
              placeholder="e.g. Retail performance"
              value={name}
              onChange={(e) => setName(e.currentTarget.value)}
              disabled={isPending}
              autoFocus
              required
          />

          {!project && (
              <Textarea
                  mt="md"
                  label="What are you exploring?"
                  description="Optional"
                  placeholder="A short note about the question or topic behind this project."
                  value={description}
                  onChange={(e) => setDescription(e.currentTarget.value)}
                  disabled={isPending}
                  autosize
                  minRows={3}
              />
          )}

          {error && (
              <Alert color="red" mt="md">
                {error instanceof Error
                    ? error.message
                    : "Something went wrong. Please try again."}
              </Alert>
          )}

          <div className="dialog-actions">
            <Button
                variant="default"
                type="button"
                disabled={isPending}
                onClick={onClose}
            >
              Cancel
            </Button>

            <Button
                type="submit"
                loading={isPending}
                disabled={!name.trim()}
            >
              {project ? "Save changes" : "Create project"}
            </Button>
          </div>
        </form>
      </Modal>
  );
}

function ProjectTile({
                       project,
                       index,
                       onRename,
                       onDelete,
                     }: {
  project: Project;
  index: number;
  onRename: () => void;
  onDelete: () => void;
}) {
  const datasets = useDatasets(project.id);
  const dashboards = useDashboards(project.id);
  return (
      <article className={`project-card project-tone-${index % 3}`}>
        <Link to={`/projects/${project.id}`} className="project-card-link">
          <div className="project-preview">
            <div className="preview-window">
              <div className="preview-window-head">
                <span />
                <span />
                <span />
                <i>PROJECT OVERVIEW</i>
              </div>
              <div className="project-preview-template" aria-hidden="true">
                <div className="preview-template-kpi">
                  <span />
                  <strong />
                </div>

                <div className="preview-template-chart">
                  <span />
                  <span />
                  <span />
                  <span />
                  <span />
                  <span />
                </div>

                <div className="preview-template-lines">
                  <span />
                  <span />
                  <span />
                </div>
              </div>
            </div>
            <span className="project-preview-tag">
            {datasets.data?.length ? "IN PROGRESS" : "GETTING STARTED"}
          </span>
          </div>
          <div className="project-card-content">
            <h3>{project.name}</h3>
            <p>
              {project.description ||
                  "A shared home for your data and your next discovery."}
            </p>
            <div className="project-card-meta">
            <span>
              <Icon name="data" size={14} />
              {datasets.data?.length ?? "—"}{" "}
              {datasets.data?.length === 1 ? "dataset" : "datasets"}
            </span>
              <span>
              <Icon name="dashboard" size={14} />
                {dashboards.data?.length ?? "—"}{" "}
                {dashboards.data?.length === 1 ? "dashboard" : "dashboards"}
            </span>
            </div>
          </div>
        </Link>
        <div className="project-card-footer">
          <span>Created {formatDate(project.createdAtUtc)}</span>
          <Menu position="bottom-end" shadow="md">
            <Menu.Target>
              <ActionIcon
                  variant="subtle"
                  color="gray"
                  aria-label={`Options for ${project.name}`}
              >
                <Icon name="more" />
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item
                  leftSection={<Icon name="edit" size={15} />}
                  onClick={onRename}
              >
                Rename project
              </Menu.Item>
              <Menu.Item
                  color="red"
                  leftSection={<Icon name="trash" size={15} />}
                  onClick={onDelete}
              >
                Delete project
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </div>
      </article>
  );
}

export default function ProjectsPage() {
  const projects = useProjects();
  const [createOpen, setCreateOpen] = useState(false);
  const [renaming, setRenaming] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);
  const [deleteName, setDeleteName] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<string | null>("newest");
  const [view, setView] = useState("grid");
  const remove = useDeleteProject();
  const filtered = projects.data
      ?.filter((p) => p.name.toLowerCase().includes(search.toLowerCase()))
      .sort((a, b) =>
          sort === "name"
              ? a.name.localeCompare(b.name)
              : b.createdAtUtc.localeCompare(a.createdAtUtc),
      );
  return (
      <div className="page-content projects-center">
        <PageHeading
            eyebrow="YOUR WORKSPACE"
            title="Projects"
            description="Keep your datasets, analysis, and dashboards organized by project."
            actions={
              <Button
                  onClick={() => setCreateOpen(true)}
                  leftSection={<Icon name="plus" size={17} />}
              >
                New project
              </Button>
            }
        />
        {!projects.isPending && !projects.error && projects.data?.length === 0 && (
            <section className="welcome-banner">
              <div className="welcome-copy">
            <span className="editorial-label">
              <span className="tiny-dot" /> FROM THE FIRST ROW TO THE BIGGER
              PICTURE
            </span>

                <h2>
                  Start with a project.
                  <br />
                  Build from there.
                </h2>

                <p>
                  Keep related data, analysis, and dashboards together
                  <br className="desktop-br" /> as your work grows.
                </p>

                <button className="text-link" onClick={() => setCreateOpen(true)}>
                  Create your first project <Icon name="arrow" size={17} />
                </button>
              </div>

              <div className="workflow-art" aria-hidden="true">
                <div className="workflow-art-grid" />

                <svg
                    className="workflow-lines"
                    viewBox="0 0 1000 330"
                    preserveAspectRatio="none"
                    aria-hidden="true"
                >
                  <path d="M 349 56 C 430 60, 520 158, 599 158" />
                  <path d="M 770 195 C 760 238, 575 260, 519 250" />
                </svg>

                <div className="workflow-node node-data">
              <span>
                <Icon name="data" size={19} />
              </span>
                  <div>
                    <small>01 / BRING IT TOGETHER</small>
                    <strong>Your data</strong>
                  </div>
                  <i />
                </div>

                <div className="workflow-node node-insight">
              <span>
                <Icon name="chart" size={19} />
              </span>
                  <div>
                    <small>02 / FIND THE PATTERN</small>
                    <strong>A new perspective</strong>
                  </div>
                  <i />
                </div>

                <div className="workflow-node node-story">
              <span>
                <Icon name="dashboard" size={19} />
              </span>
                  <div>
                    <small>03 / MAKE IT MEAN SOMETHING</small>
                    <strong>The bigger picture</strong>
                  </div>
                </div>

                <div className="art-annotation">
                  a little curiosity goes a long way <span>↗</span>
                </div>
              </div>
            </section>
        )}

        {!!projects.data?.length && (
            <div className="projects-toolbar">
              <div className="section-heading">
                <h2>Your projects</h2>
                <span className="count-badge">{projects.data.length}</span>
              </div>

              <div className="project-controls">
                <TextInput
                    aria-label="Search projects"
                    placeholder="Search projects…"
                    value={search}
                    onChange={(e) => setSearch(e.currentTarget.value)}
                    leftSection={<Icon name="search" size={15} />}
                />

                <Select
                    aria-label="Sort projects"
                    value={sort}
                    onChange={setSort}
                    data={[
                      { value: "newest", label: "Newest first" },
                      { value: "name", label: "Name A–Z" },
                    ]}
                    allowDeselect={false}
                />

                <div className="view-switch">
                  <Tooltip label="Grid view">
                    <ActionIcon
                        variant={view === "grid" ? "white" : "subtle"}
                        color="gray"
                        aria-label="Grid view"
                        aria-pressed={view === "grid"}
                        onClick={() => setView("grid")}
                    >
                      <Icon name="grid" size={16} />
                    </ActionIcon>
                  </Tooltip>

                  <Tooltip label="List view">
                    <ActionIcon
                        variant={view === "list" ? "white" : "subtle"}
                        color="gray"
                        aria-label="List view"
                        aria-pressed={view === "list"}
                        onClick={() => setView("list")}
                    >
                      <Icon name="list" size={17} />
                    </ActionIcon>
                  </Tooltip>
                </div>
              </div>
            </div>
        )}

        {projects.isPending ? (
            <LoadingState />
        ) : projects.error ? (
            <ErrorState
                error={projects.error}
                retry={() => void projects.refetch()}
            />
        ) : projects.data?.length === 0 ? null : !filtered?.length ? (
            <EmptyState
                title="No matching projects"
                description="Try a different name or clear your search."
                action={
                  <Button variant="default" onClick={() => setSearch("")}>
                    Clear search
                  </Button>
                }
            />
        ) : (
            <div
                className={`projects-grid ${view === "list" ? "projects-list" : ""}`}
            >
              {filtered.map((p, i) => (
                  <ProjectTile
                      key={p.id}
                      project={p}
                      index={i}
                      onRename={() => setRenaming(p)}
                      onDelete={() => {
                        setDeleting(p);
                        setDeleteName("");
                        remove.reset();
                      }}
                  />
              ))}
            </div>
        )}
        <div className="project-hint">
          <Icon name="lock" size={14} />
          <p>
            Your projects are private. Each one can hold multiple datasets and
            dashboards.
          </p>
        </div>
        {createOpen && (
            <ProjectDialog opened onClose={() => setCreateOpen(false)} />
        )}{" "}
        {renaming && (
            <ProjectDialog
                key={renaming.id}
                project={renaming}
                opened
                onClose={() => setRenaming(null)}
            />
        )}
        <Modal
            opened={!!deleting}
            onClose={() => {
              if (!remove.isPending) setDeleting(null);
            }}
            closeOnClickOutside={!remove.isPending}
            closeOnEscape={!remove.isPending}
            withCloseButton={!remove.isPending}
            title="Delete this project?"
            centered
        >
          <p className="muted">
            This permanently removes <strong>{deleting?.name}</strong> and its
            datasets, dashboards, and charts. This action cannot be undone.
          </p>
          <TextInput
              mt="lg"
              label={`Type “${deleting?.name}” to confirm`}
              value={deleteName}
              disabled={remove.isPending}
              onChange={(e) => setDeleteName(e.currentTarget.value)}
          />
          {remove.error && (
              <Alert color="red" mt="md">
                {remove.error.message}
              </Alert>
          )}
          <div className="dialog-actions">
            <Button
                variant="default"
                disabled={remove.isPending}
                onClick={() => setDeleting(null)}
            >
              Keep project
            </Button>
            <Button
                color="red"
                disabled={deleteName !== deleting?.name}
                loading={remove.isPending}
                onClick={() => {
                  if (deleting) {
                    remove.mutate(deleting.id, {
                      onSuccess: () => setDeleting(null),
                    });
                  }
                }}
            >
              Delete project
            </Button>
          </div>
        </Modal>
      </div>
  );
}

function DatasetQualityItem({
                              projectId,
                              dataset,
                            }: {
  projectId: string;
  dataset: Dataset;
}) {
  const profile = useDatasetProfile(
      projectId,
      dataset.id,
      Boolean(dataset.currentVersion),
  );

  if (!dataset.currentVersion) {
    return (
        <div className="quality-dataset-row is-pending">
        <span className="quality-dataset-icon">
          <Icon name="data" size={18} />
        </span>

          <div>
            <strong>{dataset.name}</strong>
            <small>Waiting for a ready version before quality can be checked.</small>
          </div>

          <span className="pill pill-neutral">Pending</span>
        </div>
    );
  }

  if (profile.isPending) {
    return (
        <div className="quality-dataset-row">
        <span className="quality-dataset-icon">
          <Icon name="data" size={18} />
        </span>

          <div>
            <strong>{dataset.name}</strong>
            <small>Checking data quality…</small>
          </div>
        </div>
    );
  }

  if (profile.error || !profile.data) {
    return (
        <div className="quality-dataset-row has-issues">
        <span className="quality-dataset-icon">
          <Icon name="warning" size={18} />
        </span>

          <div>
            <strong>{dataset.name}</strong>
            <small>Quality profile could not be loaded.</small>
          </div>

          <Button
              variant="subtle"
              size="compact-xs"
              onClick={() => void profile.refetch()}
          >
            Retry
          </Button>
        </div>
    );
  }

  const missing = profile.data.columns.reduce(
      (total, column) => total + column.nullCount,
      0,
  );

  return (
      <Link
          className={`quality-dataset-row ${missing ? "has-issues" : ""}`}
          to={`/projects/${projectId}/data/${dataset.id}`}
      >
      <span className="quality-dataset-icon">
        <Icon name={missing ? "warning" : "check"} size={18} />
      </span>

        <div>
          <strong>{dataset.name}</strong>
          <small>
            {missing
                ? `${missing.toLocaleString()} missing ${
                    missing === 1 ? "value" : "values"
                } detected`
                : "No missing values detected"}
          </small>
        </div>

        <span className={`pill ${missing ? "pill-neutral" : ""}`}>
        {missing ? "Needs attention" : "Checked"}
      </span>

        <Icon name="chevron" size={15} />
      </Link>
  );
}

export function ProjectOverviewPage() {
  const { projectId = "" } = useParams();

  const [importOpen, setImportOpen] = useState(false);
  const [deletingDataset, setDeletingDataset] = useState<Dataset | null>(null);
  const [deletingDashboard, setDeletingDashboard] =
      useState<Dashboard | null>(null);

  const datasets = useDatasets(projectId);
  const dashboards = useDashboards(projectId);
  const datasetUsage = useDatasetUsage(projectId, deletingDataset?.id);
  const removeDataset = useDeleteDataset(projectId);
  const removeDashboard = useDeleteDashboard(projectId);

  const ready =
      datasets.data?.filter((dataset) => dataset.currentVersion) ?? [];

  const rowCount = ready.reduce(
      (total, dataset) => total + (dataset.currentVersion?.rowCount ?? 0),
      0,
  );

  const dataPath = `/projects/${projectId}/data`;

  const isEmptyProject =
      !datasets.isPending &&
      !dashboards.isPending &&
      (datasets.data?.length ?? 0) === 0 &&
      (dashboards.data?.length ?? 0) === 0;

  return (
      <div className="page-content overview-page">
        <PageHeading
            title="Overview"
            description="Your project's data, analysis, and dashboards in one place."
            actions={
              <Button
                  leftSection={<Icon name="plus" size={16} />}
                  onClick={() => setImportOpen(true)}
              >
                Add data
              </Button>
            }
        />

        <div className="overview-metrics">
          <div>
          <span>
            <Icon name="data" size={17} /> Datasets
          </span>
            <strong>{datasets.data?.length ?? "—"}</strong>
            <small>{ready.length} ready to work with</small>
          </div>

          <div>
          <span>
            <Icon name="list" size={17} /> Rows
          </span>
            <strong>{rowCount.toLocaleString()}</strong>
            <small>Across ready datasets</small>
          </div>

          <div>
          <span>
            <Icon name="dashboard" size={17} /> Dashboards
          </span>
            <strong>{dashboards.data?.length ?? "—"}</strong>
            <small>Created in this project</small>
          </div>
        </div>

        {isEmptyProject && (
            <>
              <div className="section-heading overview-section-heading">
                <h2>Where would you like to start?</h2>
                <span className="muted">One step leads to the next.</span>
              </div>

              <div className="journey-cards">
                <button onClick={() => setImportOpen(true)}>
                  <span className="journey-step">01</span>
                  <span className="journey-icon">
                <Icon name="upload" size={22} />
              </span>
                  <h3>Bring in your data</h3>
                  <p>
                    Add a data source and we’ll help you inspect what’s inside.
                  </p>
                  <span className="text-link">
                Add data <Icon name="arrow" size={16} />
              </span>
                </button>

                <Link to={dataPath}>
                  <span className="journey-step">02</span>
                  <span className="journey-icon">
                <Icon name="chart" size={22} />
              </span>
                  <h3>Prepare and understand it</h3>
                  <p>
                    Review columns, profile quality, and prepare your data for
                    analysis.
                  </p>
                  <span className="text-link">
                Open data workspace <Icon name="arrow" size={16} />
              </span>
                </Link>

                <Link to={`/projects/${projectId}/dashboards`}>
                  <span className="journey-step">03</span>
                  <span className="journey-icon">
                <Icon name="dashboard" size={22} />
              </span>
                  <h3>Build the bigger picture</h3>
                  <p>
                    Turn your analysis into dashboards you can return to and share.
                  </p>
                  <span className="text-link">
                Open dashboards <Icon name="arrow" size={16} />
              </span>
                </Link>
              </div>
            </>
        )}

        {!!datasets.data?.length && (
            <section className="quality-panel">
              <div className="panel-heading quality-panel-heading">
                <div>
                  <h2>Data quality</h2>
                  <p>
                    Review detected issues dataset by dataset before you build on
                    top of them.
                  </p>
                </div>

                <Link to={dataPath} className="text-link">
                  View all data <Icon name="arrow" size={15} />
                </Link>
              </div>

              <div className="quality-dataset-list">
                {datasets.data.map((dataset) => (
                    <DatasetQualityItem
                        key={dataset.id}
                        projectId={projectId}
                        dataset={dataset}
                    />
                ))}
              </div>
            </section>
        )}

        {!isEmptyProject && (
            <div className="overview-bottom-grid">
              <section className="panel">
                <div className="panel-heading">
                  <h2>Your data</h2>
                  <Link to={dataPath} className="text-link">
                    View all <Icon name="arrow" size={15} />
                  </Link>
                </div>

                {datasets.isPending ? (
                    <LoadingState />
                ) : datasets.error ? (
                    <ErrorState
                        error={datasets.error}
                        retry={() => void datasets.refetch()}
                    />
                ) : !datasets.data?.length ? (
                    <EmptyState
                        icon="data"
                        title="No datasets yet"
                        description="Add a data source to start preparing and exploring your data."
                        action={
                          <Button variant="default" onClick={() => setImportOpen(true)}>
                            Add data
                          </Button>
                        }
                    />
                ) : (
                    datasets.data.slice(0, 4).map((dataset) => (
                        <div className="resource-row" key={dataset.id}>
                          <Link
                              to={`${dataPath}/${dataset.id}`}
                              className="resource-row-main"
                          >
                    <span className="resource-icon">
                      <Icon name="data" size={19} />
                    </span>

                            <div>
                              <strong>{dataset.name}</strong>
                              <small>
                                {dataset.currentVersion
                                    ? `${dataset.currentVersion.rowCount.toLocaleString()} rows · ${dataset.currentVersion.columnCount} columns`
                                    : "Waiting for a ready version"}
                              </small>
                            </div>

                            <span
                                className={`pill ${
                                    !dataset.currentVersion ? "pill-neutral" : ""
                                }`}
                            >
                      {dataset.currentVersion ? "Ready" : "Pending"}
                    </span>
                          </Link>

                          <Menu position="bottom-end" shadow="md">
                            <Menu.Target>
                              <ActionIcon
                                  variant="subtle"
                                  color="gray"
                                  aria-label={`Options for ${dataset.name}`}
                              >
                                <Icon name="more" />
                              </ActionIcon>
                            </Menu.Target>

                            <Menu.Dropdown>
                              <Menu.Item
                                  component={Link}
                                  to={`${dataPath}/${dataset.id}`}
                                  leftSection={<Icon name="data" size={15} />}
                              >
                                Open dataset
                              </Menu.Item>

                              <Menu.Item
                                  component={Link}
                                  to={`/projects/${projectId}/explore?dataset=${dataset.id}`}
                                  disabled={!dataset.currentVersion}
                                  leftSection={<Icon name="chart" size={15} />}
                              >
                                Explore dataset
                              </Menu.Item>

                              <Menu.Divider />

                              <Menu.Item
                                  color="red"
                                  leftSection={<Icon name="trash" size={15} />}
                                  onClick={() => {
                                    removeDataset.reset();
                                    setDeletingDataset(dataset);
                                  }}
                              >
                                Delete dataset
                              </Menu.Item>
                            </Menu.Dropdown>
                          </Menu>
                        </div>
                    ))
                )}
              </section>

              <section className="panel">
                <div className="panel-heading">
                  <h2>Your dashboards</h2>
                  <Link
                      to={`/projects/${projectId}/dashboards`}
                      className="text-link"
                  >
                    View all <Icon name="arrow" size={15} />
                  </Link>
                </div>

                {dashboards.isPending ? (
                    <LoadingState />
                ) : dashboards.error ? (
                    <ErrorState
                        error={dashboards.error}
                        retry={() => void dashboards.refetch()}
                    />
                ) : !dashboards.data?.length ? (
                    <EmptyState
                        icon="dashboard"
                        title="No dashboards yet"
                        description="Create a dashboard when you are ready to bring your analysis together."
                        action={
                          <Button
                              component={Link}
                              to={`/projects/${projectId}/dashboards`}
                              variant="default"
                          >
                            Create a dashboard
                          </Button>
                        }
                    />
                ) : (
                    dashboards.data.slice(0, 4).map((dashboard) => (
                        <div className="resource-row" key={dashboard.id}>
                          <Link
                              to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                              className="resource-row-main"
                          >
                    <span className="resource-icon">
                      <Icon name="dashboard" size={19} />
                    </span>

                            <div>
                              <strong>{dashboard.name}</strong>
                              <small>Created {formatDate(dashboard.createdAtUtc)}</small>
                            </div>
                          </Link>

                          <Menu position="bottom-end" shadow="md">
                            <Menu.Target>
                              <ActionIcon
                                  variant="subtle"
                                  color="gray"
                                  aria-label={`Options for ${dashboard.name}`}
                              >
                                <Icon name="more" />
                              </ActionIcon>
                            </Menu.Target>

                            <Menu.Dropdown>
                              <Menu.Item
                                  component={Link}
                                  to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                                  leftSection={<Icon name="edit" size={15} />}
                              >
                                Edit dashboard
                              </Menu.Item>

                              <Menu.Divider />

                              <Menu.Item
                                  color="red"
                                  leftSection={<Icon name="trash" size={15} />}
                                  onClick={() => {
                                    removeDashboard.reset();
                                    setDeletingDashboard(dashboard);
                                  }}
                              >
                                Delete dashboard
                              </Menu.Item>
                            </Menu.Dropdown>
                          </Menu>
                        </div>
                    ))
                )}
              </section>
            </div>
        )}

        <ImportDataDialog
            projectId={projectId}
            opened={importOpen}
            onClose={() => setImportOpen(false)}
        />

        <Modal
            opened={!!deletingDataset}
            onClose={() => {
              if (!removeDataset.isPending) setDeletingDataset(null);
            }}
            title="Delete this dataset?"
            centered
            closeOnClickOutside={!removeDataset.isPending}
            closeOnEscape={!removeDataset.isPending}
            withCloseButton={!removeDataset.isPending}
        >
          <p className="overview-dialog-description">
            “{deletingDataset?.name}” and its data will be permanently removed.
            This cannot be undone.
          </p>

          {datasetUsage.isPending && (
              <p className="overview-dialog-description" role="status">
                Checking whether dashboards use this dataset…
              </p>
          )}

          {datasetUsage.isError && (
              <ErrorState
                  error={datasetUsage.error}
                  retry={() => void datasetUsage.refetch()}
              />
          )}

          {!!datasetUsage.data?.length && (
              <Alert
                  color="orange"
                  title="This dataset is still in use"
                  icon={<Icon name="warning" />}
              >
                Remove its charts from these dashboards before deleting the
                dataset:
                <ul className="overview-usage-list">
                  {datasetUsage.data.map(({ dashboard, count }) => (
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

          {removeDataset.error && <ErrorState error={removeDataset.error} />}

          <div className="dialog-actions">
            <Button
                variant="default"
                disabled={removeDataset.isPending}
                onClick={() => setDeletingDataset(null)}
            >
              Keep dataset
            </Button>

            <Button
                color="red"
                loading={removeDataset.isPending}
                disabled={
                    datasetUsage.isPending ||
                    datasetUsage.isError ||
                    datasetUsage.isFetching ||
                    !!datasetUsage.data?.length
                }
                onClick={() => {
                  if (deletingDataset) {
                    removeDataset.mutate(deletingDataset.id, {
                      onSuccess: () => setDeletingDataset(null),
                    });
                  }
                }}
            >
              Delete dataset
            </Button>
          </div>
        </Modal>

        <Modal
            opened={!!deletingDashboard}
            onClose={() => {
              if (!removeDashboard.isPending) setDeletingDashboard(null);
            }}
            title="Delete this dashboard?"
            centered
            closeOnClickOutside={!removeDashboard.isPending}
            closeOnEscape={!removeDashboard.isPending}
            withCloseButton={!removeDashboard.isPending}
        >
          <p className="overview-dialog-description">
            “{deletingDashboard?.name}” will be permanently removed. This action
            cannot be undone.
          </p>

          {removeDashboard.error && <ErrorState error={removeDashboard.error} />}

          <div className="dialog-actions">
            <Button
                variant="default"
                disabled={removeDashboard.isPending}
                onClick={() => setDeletingDashboard(null)}
            >
              Keep dashboard
            </Button>

            <Button
                color="red"
                loading={removeDashboard.isPending}
                onClick={() => {
                  if (deletingDashboard) {
                    removeDashboard.mutate(deletingDashboard.id, {
                      onSuccess: () => setDeletingDashboard(null),
                    });
                  }
                }}
            >
              Delete dashboard
            </Button>
          </div>
        </Modal>
      </div>
  );
}
