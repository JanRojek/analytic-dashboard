import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Button, Modal, Select, TextInput } from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { ErrorState } from "../../../components/Feedback";
import type { WidgetInput } from "../../../data/types";

import {
  useCreateDashboard,
  useCreateWidget,
  useDashboards,
} from "../../dashboards/hooks";

export function SaveChartDialog({
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
  const createDashboard = useCreateDashboard(projectId);
  const createWidget = useCreateWidget(projectId);
  const navigate = useNavigate();
  const [target, setTarget] = useState<string | null>(null);
  const [name, setName] = useState("");
  const dashboards = useDashboards(projectId, opened);
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

      await createWidget.mutateAsync({
        dashboardId,
        input,
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
