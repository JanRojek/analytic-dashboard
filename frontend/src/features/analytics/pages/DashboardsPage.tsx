import { useState } from "react";
import { ActionIcon, Button, Menu, Modal, TextInput } from "@mantine/core";
import { Link, useNavigate, useParams } from "react-router-dom";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import type { Dashboard } from "../../../data/types";
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
