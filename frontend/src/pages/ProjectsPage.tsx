import { useState, type FormEvent } from "react";
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
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../data/session";
import type { Project } from "../data/types";
import { Icon } from "../components/Icon";
import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../components/Feedback";
import { ImportDataDialog } from "../components/ImportDataDialog";
import { Chart } from "../components/Charts";

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
  const { adapter, mode } = useSession();
  const client = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState(project?.name || "");
  const [description, setDescription] = useState("");
  const mutation = useMutation({
    mutationFn: () =>
      project
        ? adapter.renameProject(project.id, name.trim())
        : adapter.createProject(name.trim(), description.trim()),
    onSuccess: async (p) => {
      await client.invalidateQueries({ queryKey: ["projects"] });
      await client.invalidateQueries({ queryKey: ["project", p.id] });
      onClose();
      if (!project) navigate(`/projects/${p.id}`);
    },
  });
  function submit(e: FormEvent) {
    e.preventDefault();
    if (name.trim() && !mutation.isPending) mutation.mutate();
  }
  return (
    <Modal
      opened={opened}
      onClose={() => {
        if (!mutation.isPending) onClose();
      }}
      closeOnClickOutside={!mutation.isPending}
      closeOnEscape={!mutation.isPending}
      withCloseButton={!mutation.isPending}
      title={project ? "Rename project" : "Make room for a new question"}
      centered
      size="md"
    >
      <form onSubmit={submit}>
        <div className="new-project-intro">
          <span className="empty-icon">
            <Icon name="folder" size={25} />
          </span>
          <p>
            A project brings related datasets, explorations, and dashboards
            together. Start with a name that means something to you.
          </p>
        </div>
        <TextInput
          data-autofocus
          label="Project name"
          placeholder="e.g. Retail performance"
          value={name}
          disabled={mutation.isPending}
          onChange={(e) => setName(e.currentTarget.value)}
          required
          maxLength={100}
        />
        {mode === "demo" && !project && (
          <Textarea
            label="What are you exploring?"
            description="Optional · Give this project a little context."
            placeholder="Understand what drives our sales…"
            mt="md"
            minRows={3}
            value={description}
            disabled={mutation.isPending}
            onChange={(e) => setDescription(e.currentTarget.value)}
            maxLength={400}
          />
        )}
        {mutation.error && (
          <Alert role="alert" color="red" mt="md">
            {mutation.error.message}
          </Alert>
        )}
        <div className="dialog-actions">
          <Button
            variant="default"
            disabled={mutation.isPending}
            onClick={onClose}
          >
            Cancel
          </Button>
          <Button
            type="submit"
            disabled={!name.trim()}
            loading={mutation.isPending}
            rightSection={<Icon name="arrow" size={16} />}
          >
            {project ? "Save name" : "Create project"}
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
  const { adapter } = useSession();
  const datasets = useQuery({
    queryKey: ["datasets", project.id],
    queryFn: () => adapter.listDatasets(project.id),
  });
  const dashboards = useQuery({
    queryKey: ["dashboards", project.id],
    queryFn: () => adapter.listDashboards(project.id),
  });
  const boardId = dashboards.data?.[0]?.id;
  const widgets = useQuery({
    queryKey: ["widgets", project.id, boardId],
    queryFn: () => adapter.listWidgets(project.id, boardId!),
    enabled: !!boardId,
  });
  const previewWidget = widgets.data?.find((w) => w.type !== "Kpi");
  const values = useQuery({
    queryKey: ["widget-data", project.id, boardId, previewWidget?.id],
    queryFn: () => adapter.widgetData(project.id, boardId!, previewWidget!.id),
    enabled: !!previewWidget,
  });
  return (
    <article className={`project-card project-tone-${index % 3}`}>
      <Link to={`/projects/${project.id}`} className="project-card-link">
        <div className="project-preview">
          <div className="preview-window">
            <div className="preview-window-head">
              <span />
              <span />
              <span />
              <i>{boardId ? "YOUR LATEST PERSPECTIVE" : "A CLEAN SLATE"}</i>
            </div>
            {values.data?.length ? (
              <Chart type={previewWidget!.type} items={values.data} compact />
            ) : (
              <div className="preview-empty">
                <Icon
                  name={datasets.data?.length ? "data" : "spark"}
                  size={29}
                />
                <span>
                  {datasets.data?.length
                    ? "Ready for a discovery"
                    : "Possibility starts here"}
                </span>
                <div className="preview-placeholder-lines">
                  <span />
                  <span />
                  <span />
                </div>
              </div>
            )}
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
  const { adapter } = useSession();
  const client = useQueryClient();
  const projects = useQuery({
    queryKey: ["projects"],
    queryFn: () => adapter.listProjects(),
  });
  const [createOpen, setCreateOpen] = useState(false);
  const [renaming, setRenaming] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);
  const [deleteName, setDeleteName] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<string | null>("newest");
  const [view, setView] = useState("grid");
  const remove = useMutation({
    mutationFn: () => adapter.deleteProject(deleting!.id),
    onSuccess: async () => {
      if (deleting)
        client.removeQueries({
          predicate: (query) => query.queryKey[1] === deleting.id,
        });
      setDeleting(null);
      await client.invalidateQueries({ queryKey: ["projects"] });
    },
  });
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
        title="Good questions start here."
        description="A home for your data, your discoveries, and what comes next."
        actions={
          <Button
            onClick={() => setCreateOpen(true)}
            leftSection={<Icon name="plus" size={17} />}
          >
            New project
          </Button>
        }
      />
      <section className="welcome-banner">
        <div className="welcome-copy">
          <span className="editorial-label">
            <span className="tiny-dot" /> FROM THE FIRST ROW TO THE BIGGER
            PICTURE
          </span>
          <h2>
            Less friction.
            <br />
            More perspective.
          </h2>
          <p>
            Bring your data into focus. Each project gives your ideas
            <br className="desktop-br" /> a space to grow from raw data into a
            clear story.
          </p>
          <button className="text-link" onClick={() => setCreateOpen(true)}>
            Start something new <Icon name="arrow" size={17} />
          </button>
        </div>
        <div className="workflow-art" aria-hidden="true">
          <div className="workflow-art-grid" />
          <div className="workflow-connection one" />
          <div className="workflow-connection two" />
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
      <div className="projects-toolbar">
        <div className="section-heading">
          <h2>Your projects</h2>
          <span className="count-badge">{projects.data?.length ?? "—"}</span>
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
      {projects.isPending ? (
        <LoadingState />
      ) : projects.error ? (
        <ErrorState
          error={projects.error}
          retry={() => void projects.refetch()}
        />
      ) : !filtered?.length ? (
        <EmptyState
          title={
            search
              ? "No matching projects"
              : "Your first question deserves a home."
          }
          description={
            search
              ? "Try a different name or clear your search."
              : "Create a project, bring in a CSV, and see where it takes you."
          }
          action={
            <Button
              variant={search ? "default" : "filled"}
              onClick={() => (search ? setSearch("") : setCreateOpen(true))}
            >
              {search ? "Clear search" : "Create your first project"}
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
            onClick={() => remove.mutate()}
          >
            Delete project
          </Button>
        </div>
      </Modal>
    </div>
  );
}

export function ProjectOverviewPage() {
  const { projectId = "" } = useParams();
  const { adapter } = useSession();
  const [importOpen, setImportOpen] = useState(false);
  const datasets = useQuery({
    queryKey: ["datasets", projectId],
    queryFn: () => adapter.listDatasets(projectId),
  });
  const dashboards = useQuery({
    queryKey: ["dashboards", projectId],
    queryFn: () => adapter.listDashboards(projectId),
  });
  const ready = datasets.data?.filter((d) => d.currentVersion) || [];
  const first = ready[0];
  const profile = useQuery({
    queryKey: ["profile", projectId, first?.id],
    queryFn: () => adapter.getProfile(projectId, first!.id),
    enabled: !!first,
  });
  const missing =
    profile.data?.columns.reduce((total, c) => total + c.nullCount, 0) || 0;
  const dataPath = `/projects/${projectId}/data`;
  return (
    <div className="page-content overview-page">
      <PageHeading
        title="The bigger picture."
        description="Your data, questions, and dashboards. All connected."
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
          <small>{ready.length} ready to explore</small>
        </div>
        <div>
          <span>
            <Icon name="list" size={17} /> Rows of possibility
          </span>
          <strong>
            {ready
              .reduce((n, d) => n + (d.currentVersion?.rowCount || 0), 0)
              .toLocaleString()}
          </strong>
          <small>Across your ready datasets</small>
        </div>
        <div>
          <span>
            <Icon name="dashboard" size={17} /> Dashboards
          </span>
          <strong>{dashboards.data?.length ?? "—"}</strong>
          <small>Your perspectives, in one place</small>
        </div>
      </div>
      <div className="section-heading overview-section-heading">
        <h2>Where would you like to go?</h2>
        <span className="muted">One step leads to the next.</span>
      </div>
      <div className="journey-cards">
        <button onClick={() => setImportOpen(true)}>
          <span className="journey-step">01</span>
          <span className="journey-icon">
            <Icon name="upload" size={22} />
          </span>
          <h3>Bring in your data</h3>
          <p>Start with a CSV. We’ll help you get to know what’s inside.</p>
          <span className="text-link">
            Add a dataset <Icon name="arrow" size={16} />
          </span>
        </button>
        <Link to={ready.length ? `/projects/${projectId}/explore` : dataPath}>
          <span className="journey-step">02</span>
          <span className="journey-icon">
            <Icon name="chart" size={22} />
          </span>
          <h3>Follow a question</h3>
          <p>Spot a pattern. Compare a category. Find your next insight.</p>
          <span className="text-link">
            {ready.length ? "Explore your data" : "Start with a dataset"}{" "}
            <Icon name="arrow" size={16} />
          </span>
        </Link>
        <Link to={`/projects/${projectId}/dashboards`}>
          <span className="journey-step">03</span>
          <span className="journey-icon">
            <Icon name="dashboard" size={22} />
          </span>
          <h3>Give it a canvas</h3>
          <p>Bring your best discoveries together into a clear story.</p>
          <span className="text-link">
            Open dashboards <Icon name="arrow" size={16} />
          </span>
        </Link>
      </div>
      {first && profile.data && (
        <Link
          className={`quality-callout ${missing ? "has-missing" : ""}`}
          to={`${dataPath}/${first.id}`}
        >
          <span className="quality-callout-icon">
            <Icon name={missing ? "shield" : "check"} size={22} />
          </span>
          <div>
            <strong>
              {missing
                ? "A little attention goes a long way."
                : "A solid foundation for your next question."}
            </strong>
            <p>
              {missing
                ? `${first.name} has ${missing.toLocaleString()} missing values. Review its profile before you explore.`
                : `${first.name} has no missing values across ${profile.data.columnCount} columns. Take a closer look.`}
            </p>
          </div>
          <span className="text-link">
            Review data <Icon name="arrow" size={16} />
          </span>
        </Link>
      )}
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
              title="A fresh starting point"
              description="Your first dataset is the beginning of the story."
              action={
                <Button variant="default" onClick={() => setImportOpen(true)}>
                  Add data
                </Button>
              }
            />
          ) : (
            datasets.data.slice(0, 4).map((d) => (
              <Link
                to={`${dataPath}/${d.id}`}
                className="resource-row"
                key={d.id}
              >
                <span className="resource-icon">
                  <Icon name="data" size={19} />
                </span>
                <div>
                  <strong>{d.name}</strong>
                  <small>
                    {d.currentVersion
                      ? `${d.currentVersion.rowCount.toLocaleString()} rows · ${d.currentVersion.columnCount} columns`
                      : "Waiting for a ready version"}
                  </small>
                </div>
                <span
                  className={`pill ${!d.currentVersion ? "pill-neutral" : ""}`}
                >
                  {d.currentVersion ? "Ready" : "Pending"}
                </span>
                <Icon name="chevron" size={15} />
              </Link>
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
              title="Make space for insight"
              description="Your charts will feel at home on a dashboard."
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
            dashboards.data.slice(0, 4).map((d) => (
              <Link
                to={`/projects/${projectId}/dashboards/${d.id}/edit`}
                className="resource-row"
                key={d.id}
              >
                <span className="resource-icon">
                  <Icon name="dashboard" size={19} />
                </span>
                <div>
                  <strong>{d.name}</strong>
                  <small>Created {formatDate(d.createdAtUtc)}</small>
                </div>
                <Icon name="arrow" size={16} />
              </Link>
            ))
          )}
        </section>
      </div>
      <ImportDataDialog
        projectId={projectId}
        opened={importOpen}
        onClose={() => setImportOpen(false)}
      />
    </div>
  );
}
