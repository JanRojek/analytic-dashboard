import { useState } from "react";
import { Button, Modal, Select } from "@mantine/core";
import { Link } from "react-router-dom";

import {
  EmptyState,
  ErrorState,
  LoadingState,
} from "../../../components/Feedback";

import type { Dashboard, Dataset } from "../../../data/types";

import { useDatasetProfile } from "../../data/hooks";

import { NewChartForm } from "./NewChartForm";

export function NewChartDialog({
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
  const [datasetId, setDatasetId] = useState("");
  const [busy, setBusy] = useState(false);
  const current =
    datasets.find((item) => item.id === datasetId) ||
    datasets.find((item) => item.currentVersion);
  const profile = useDatasetProfile(
    dashboard.projectId,
    current?.id ?? "",
    opened && Boolean(current),
  );
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
