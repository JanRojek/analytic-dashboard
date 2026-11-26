import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { normalizePagination } from "../../../data/pagination";

import { useProjects } from "../hooks";

import {
  ActionIcon,
  Button,
  Group,
  Pagination,
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

import { ProjectTile } from "../components/ProjectTile";

import { DeleteProjectDialog } from "../components/DeleteProjectDialog";

import "../styles/projects.css";

export default function ProjectsPage() {
  const [params, setParams] = useSearchParams();
  const pagination = normalizePagination({
    page: Number(params.get("page") ?? 1),
    pageSize: Number(params.get("pageSize") ?? 24),
  });
  const projects = useProjects(pagination);
  const result = projects.data;

  useEffect(() => {
    if (!result || projects.isFetching) return;
    if (
      params.get("page") === String(result.page) &&
      params.get("pageSize") === String(result.pageSize)
    )
      return;
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set("page", String(result.page));
        next.set("pageSize", String(result.pageSize));
        return next;
      },
      { replace: true },
    );
  }, [result, projects.isFetching, params, setParams]);
  const [createOpen, setCreateOpen] = useState(false);
  const [renaming, setRenaming] = useState<Project | null>(null);
  const [deleting, setDeleting] = useState<Project | null>(null);
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<string | null>("newest");
  const [view, setView] = useState("grid");
  const filtered = projects.data?.items
    .filter((p) => p.name.toLowerCase().includes(search.toLowerCase()))
    .sort((a, b) =>
      sort === "name"
        ? a.name.localeCompare(b.name)
        : b.createdAtUtc.localeCompare(a.createdAtUtc),
    );
  function changePage(
    page: number,
    pageSize = result?.pageSize ?? pagination.pageSize,
  ) {
    setSearch("");
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.set("page", String(page));
      next.set("pageSize", String(pageSize));
      return next;
    });
  }

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
      {!projects.isPending &&
        !projects.error &&
        projects.data?.totalCount === 0 && (
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
      {!!projects.data?.totalCount && (
        <div className="projects-toolbar">
          <div className="section-heading">
            <h2>Your projects</h2>
            <span className="count-badge">{projects.data.totalCount}</span>
          </div>

          <div className="project-controls">
            <TextInput
              aria-label="Filter projects on this page"
              placeholder="Filter this page…"
              description="Filter and sort apply to this page only."
              value={search}
              onChange={(e) => setSearch(e.currentTarget.value)}
              leftSection={<Icon name="search" size={15} />}
            />

            <Select
              aria-label="Sort projects on this page"
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
      ) : projects.data?.totalCount === 0 ? null : !filtered?.length ? (
        <EmptyState
          title="No matching projects on this page"
          description="Clear the filter or browse another page."
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
              onDelete={() => setDeleting(p)}
            />
          ))}
        </div>
      )}
      {result && result.totalCount > 0 && (
        <Group justify="space-between" mt="xl" gap="md">
          <span className="muted">
            Page {result.page} of {result.totalPages} · {result.totalCount}{" "}
            projects
          </span>
          <Group gap="sm">
            {result.totalPages > 1 && (
              <Pagination
                aria-label="Project pages"
                value={result.page}
                total={result.totalPages}
                onChange={(page) => changePage(page)}
                size="sm"
              />
            )}
            <Select
              aria-label="Projects per page"
              size="xs"
              w={145}
              value={String(result.pageSize)}
              data={[...new Set([24, 48, 96, result.pageSize])]
                .sort((a, b) => a - b)
                .map((size) => ({
                  value: String(size),
                  label: `${size} per page`,
                }))}
              allowDeselect={false}
              onChange={(value) => value && changePage(1, Number(value))}
            />
          </Group>
        </Group>
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
      {deleting && (
        <DeleteProjectDialog
          project={deleting}
          onClose={() => setDeleting(null)}
        />
      )}
    </div>
  );
}
