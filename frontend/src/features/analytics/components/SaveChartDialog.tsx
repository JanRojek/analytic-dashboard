import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import {
  Button,
  Group,
  Modal,
  Pagination,
  Select,
  TextInput,
} from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { ErrorState } from "../../../components/Feedback";
import type { WidgetInput } from "../../../data/types";
import { useSession } from "../../../session/SessionContext";
import {
  useCreateDashboard,
  useCreateWidget,
  useDashboards,
  useDashboard,
} from "../../dashboards/hooks";

import { nextWidgetLayout } from "../widgetPlacement";

type SaveChartDialogProps = {
  opened: boolean;
  onClose: () => void;
  projectId: string;
  input: WidgetInput;
};

export function SaveChartDialog({
  opened,
  onClose,
  projectId,
  input,
}: SaveChartDialogProps) {
  const { adapter } = useSession();
  const navigate = useNavigate();

  const createDashboard = useCreateDashboard(projectId);
  const createWidget = useCreateWidget(projectId);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(24);
  const dashboards = useDashboards(projectId, { page, pageSize }, opened);

  const [target, setTarget] = useState<string | null>(null);
  const [name, setName] = useState("");
  const selected = useDashboard(
    projectId,
    target ?? "",
    opened && Boolean(target && target !== "__new__"),
  );
  const items = dashboards.data?.items ?? [];
  const options =
    selected.data &&
    !items.some((dashboard) => dashboard.id === selected.data.id)
      ? [selected.data, ...items]
      : items;

  const save = useMutation({
    mutationFn: async () => {
      let dashboardId = target;

      if (target === "__new__") {
        const created = await createDashboard.mutateAsync(name.trim());
        dashboardId = created.id;
        setTarget(created.id);
      }

      if (!dashboardId) {
        throw new Error("Choose a dashboard first.");
      }

      const existingWidgets = await adapter.listWidgets(projectId, dashboardId);

      await createWidget.mutateAsync({
        dashboardId,
        input: {
          ...input,
          layout: nextWidgetLayout(existingWidgets),
        },
      });

      return dashboardId;
    },
    onSuccess: (dashboardId) => {
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
              ...options.map((dashboard) => ({
                value: dashboard.id,
                label: dashboard.name,
              })),
              { value: "__new__", label: "+ Create a new dashboard" },
            ]}
            value={target}
            onChange={setTarget}
            disabled={save.isPending}
          />
        )}

        {dashboards.data && (
          <Group justify="space-between" gap="xs">
            <span className="analytics-note">
              {dashboards.data.totalCount} dashboards
            </span>
            {dashboards.data.totalPages > 1 && (
              <Pagination
                aria-label="Dashboard selection pages"
                value={dashboards.data.page}
                total={dashboards.data.totalPages}
                onChange={setPage}
                disabled={save.isPending || dashboards.isFetching}
                size="xs"
                siblings={0}
              />
            )}
            <Select
              aria-label="Dashboard options per page"
              size="xs"
              w={145}
              value={String(dashboards.data.pageSize)}
              data={[24, 48, 96].map((size) => ({
                value: String(size),
                label: `${size} per page`,
              }))}
              allowDeselect={false}
              disabled={save.isPending}
              onChange={(value) => {
                if (value) {
                  setPageSize(Number(value));
                  setPage(1);
                }
              }}
            />
          </Group>
        )}
        {target && target !== "__new__" && selected.isError && (
          <ErrorState
            error={selected.error}
            retry={() => void selected.refetch()}
          />
        )}

        {target === "__new__" && (
          <TextInput
            label="Dashboard name"
            placeholder="e.g. Commercial overview"
            maxLength={100}
            value={name}
            onChange={(event) => setName(event.currentTarget.value)}
          />
        )}

        {save.isError && <ErrorState error={save.error} />}

        <div className="analytics-dialog-actions">
          <Button variant="default" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>

          <Button
            loading={save.isPending}
            disabled={
              !target ||
              (target === "__new__"
                ? !name.trim()
                : !selected.data || selected.isError)
            }
            onClick={() => save.mutate()}
          >
            Add chart
          </Button>
        </div>
      </div>
    </Modal>
  );
}
