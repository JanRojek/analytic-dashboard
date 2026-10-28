import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Button, Modal, Select, TextInput } from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { ErrorState } from "../../../components/Feedback";
import type { WidgetInput } from "../../../data/types";
import { useSession } from "../../../session/SessionContext";
import {
  useCreateDashboard,
  useCreateWidget,
  useDashboards,
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
  const dashboards = useDashboards(projectId, opened);

  const [target, setTarget] = useState<string | null>(null);
  const [name, setName] = useState("");

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
              dashboards.isPending ? "Loading dashboards…" : "Choose a dashboard"
            }
            data={[
              ...(dashboards.data ?? []).map((dashboard) => ({
                value: dashboard.id,
                label: dashboard.name,
              })),
              { value: "__new__", label: "+ Create a new dashboard" },
            ]}
            value={target}
            onChange={setTarget}
            disabled={dashboards.isPending || save.isPending}
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
          <Button
            variant="default"
            onClick={onClose}
            disabled={save.isPending}
          >
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
