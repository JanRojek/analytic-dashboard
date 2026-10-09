import { useState } from "react";

import {
  useDeleteProject,
  useProjects
} from "../hooks";

import { useDatasets } from "../../data/hooks";

import { useDashboards } from "../../dashboards/hooks";

import { Link } from "react-router-dom";

import {
  ActionIcon,
  Alert,
  Button,
  Menu,
  Modal,
  Select,
  TextInput,
  Tooltip,
} from "@mantine/core";

import type { Project } from "../../../data/types";

import { Icon } from "../../../components/Icon";
import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";

import { ProjectDialog } from "../components/ProjectDialog";

import "../styles/projects.css";

function formatDate(value: string) {
  return new Date(value).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
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
