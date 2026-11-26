import { useEffect, useState } from "react";
import { normalizePagination } from "../../../data/pagination";
import {
  ActionIcon,
  Button,
  Group,
  Menu,
  Modal,
  Pagination,
  Select,
  TextInput,
} from "@mantine/core";
import {
  Link,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import type { DashboardListItem } from "../../../data/types";
import { editorScope, removeEditor } from "../../../mocks/editor";
import { useSession } from "../../../session/SessionContext";

import {
  useCreateDashboard,
  useDashboards,
  useDeleteDashboard,
} from "../../dashboards/hooks";

import { DashboardThumbnail } from "../components/DashboardThumbnail";

export function DashboardsPage() {
  const { projectId = "" } = useParams();
  const { mode, session } = useSession();
  const navigate = useNavigate();
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [search, setSearch] = useState("");
  const [deleting, setDeleting] = useState<DashboardListItem | null>(null);
  const [params, setParams] = useSearchParams();
  const pagination = normalizePagination({
    page: Number(params.get("page") ?? 1),
    pageSize: Number(params.get("pageSize") ?? 24),
  });
  const dashboards = useDashboards(projectId, pagination);
  const result = dashboards.data;

  useEffect(() => {
    if (!result || dashboards.isFetching) return;
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
  }, [result, dashboards.isFetching, params, setParams]);

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
  const create = useCreateDashboard(projectId);
  const remove = useDeleteDashboard(projectId);
  const filtered =
    dashboards.data?.items.filter((item) =>
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
          All dashboards <span>{dashboards.data?.totalCount ?? "—"}</span>
        </strong>
        <TextInput
          aria-label="Filter dashboards on this page"
          placeholder="Filter this page…"
          description="Filters dashboards on the current page only."
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
      ) : !dashboards.data.totalCount ? (
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
          title="No matching dashboards on this page"
          description="Clear the filter or browse another page."
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
      {result && result.totalCount > 0 && (
        <Group justify="space-between" mt="xl" gap="md">
          <span className="muted">
            Page {result.page} of {result.totalPages} · {result.totalCount}{" "}
            dashboards
          </span>
          <Group gap="sm">
            {result.totalPages > 1 && (
              <Pagination
                aria-label="Dashboard pages"
                value={result.page}
                total={result.totalPages}
                onChange={(page) => changePage(page)}
                size="sm"
              />
            )}
            <Select
              aria-label="Dashboards per page"
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
            if (name.trim())
              create.mutate(name.trim(), {
                onSuccess: (dashboard) => {
                  navigate(
                    `/projects/${projectId}/dashboards/${dashboard.id}/edit`,
                  );
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
                    } catch {
                      // The dashboard was deleted; unavailable local storage must not undo that result.
                    }

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
