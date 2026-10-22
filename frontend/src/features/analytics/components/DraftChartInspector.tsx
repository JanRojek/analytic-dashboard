import { useMutation } from "@tanstack/react-query";
import { Button, Select, TextInput } from "@mantine/core";

import { ErrorState } from "../../../components/Feedback";
import type { Dashboard, QueryConfig, WidgetInput } from "../../../data/types";
import { useSession } from "../../../session/SessionContext";
import { useDatasetProfile, useDatasets } from "../../data/hooks";
import { useCreateWidget } from "../../dashboards/hooks";

import { defaults, queryCaption } from "../chartConfig";
import type { DraftWidget } from "../draftWidgets";
import { QueryFields } from "./QueryFields";

type DraftChartInspectorProps = {
  dashboard: Dashboard;
  draft: DraftWidget;
  onChange: (patch: Partial<DraftWidget>) => void;
  onDiscard: () => void;
  onCreated: (widgetId: string) => void;
};

export function DraftChartInspector({
  dashboard,
  draft,
  onChange,
  onDiscard,
  onCreated,
}: DraftChartInspectorProps) {
  const { adapter } = useSession();
  const datasets = useDatasets(dashboard.projectId);
  const profile = useDatasetProfile(
    dashboard.projectId,
    draft.datasetId ?? "",
    Boolean(draft.datasetId),
  );
  const createWidget = useCreateWidget(dashboard.projectId);

  // Suggestions are shown without making a draft complete prematurely.
  const suggested = profile.data ? defaults(profile.data) : null;
  const query: QueryConfig | null = suggested
    ? {
        groupByColumn: draft.groupByColumn ?? suggested.groupByColumn,
        measureColumn: draft.measureColumn ?? suggested.measureColumn,
        aggregation: draft.aggregation ?? suggested.aggregation,
      }
    : null;

  const availableDatasets = (datasets.data ?? []).filter((dataset) =>
    Boolean(dataset.currentVersion),
  );
  const datasetIsAvailable = availableDatasets.some(
    (dataset) => dataset.id === draft.datasetId,
  );
  const canApply = Boolean(
    datasetIsAvailable && query?.groupByColumn && query.measureColumn,
  );

  const create = useMutation({
    mutationFn: async () => {
      if (!draft.datasetId || !query?.groupByColumn || !query.measureColumn) {
        throw new Error("Choose a dataset, group and measure first.");
      }

      const input: WidgetInput = {
        datasetId: draft.datasetId,
        type: draft.type,
        title: draft.title.trim() || queryCaption(query),
        ...query,
      };

      // Keep the same query validation as the existing NewChartForm.
      const result = await adapter.query(
        dashboard.projectId,
        draft.datasetId,
        query,
      );
      if (!result.length) {
        throw new Error(
          "This question returned no values. Try another measure.",
        );
      }

      return createWidget.mutateAsync({
        dashboardId: dashboard.id,
        input,
      });
    },
    onSuccess: (widget) => onCreated(widget.id),
  });

  return (
    <div className="inspector-section">
      <TextInput
        label="Chart title"
        placeholder="Untitled chart"
        maxLength={200}
        value={draft.title}
        disabled={create.isPending}
        onChange={(event) => onChange({ title: event.currentTarget.value })}
      />

      <span className="inspector-label">Data &amp; question</span>

      {datasets.isPending ? (
        <p className="muted">Loading project datasets…</p>
      ) : datasets.isError ? (
        <ErrorState error={datasets.error} retry={() => datasets.refetch()} />
      ) : availableDatasets.length === 0 ? (
        <p className="analytics-note">
          Import a dataset into this project before configuring your chart.
        </p>
      ) : (
        <>
          <Select
            label="Source dataset"
            placeholder="Choose a dataset"
            searchable
            data={availableDatasets.map((dataset) => ({
              value: dataset.id,
              label: dataset.name,
            }))}
            value={draft.datasetId}
            disabled={create.isPending}
            allowDeselect={false}
            onChange={(datasetId) => {
              if (!datasetId || datasetId === draft.datasetId) return;

              // Fields from the previous dataset must not leak into the next.
              onChange({
                datasetId,
                groupByColumn: null,
                measureColumn: null,
                aggregation: null,
              });
            }}
          />

          {draft.datasetId && (
            <>
              {profile.isPending ? (
                <p className="muted">Reading dataset columns…</p>
              ) : profile.isError ? (
                <ErrorState
                  error={profile.error}
                  retry={() => profile.refetch()}
                />
              ) : profile.data && query ? (
                <>
                  <QueryFields
                    profile={profile.data}
                    config={query}
                    onChange={(updated) => onChange(updated)}
                  />
                  {canApply && (
                    <p className="analytics-note">{queryCaption(query)}</p>
                  )}
                </>
              ) : null}
            </>
          )}
        </>
      )}

      {create.isError && <ErrorState error={create.error} />}

      <Button
        fullWidth
        mt="lg"
        loading={create.isPending}
        disabled={!canApply}
        onClick={() => create.mutate()}
      >
        Apply data to chart
      </Button>

      <p className="analytics-note">
        Your unfinished chart stays in this browser. Applying data creates the
        configured chart in your dashboard.
      </p>

      <Button
        fullWidth
        variant="subtle"
        color="red"
        disabled={create.isPending}
        onClick={onDiscard}
      >
        Discard draft
      </Button>
    </div>
  );
}
