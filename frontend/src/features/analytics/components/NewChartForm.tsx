import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Button, TextInput } from "@mantine/core";

import { ErrorState } from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import type {
  Dashboard,
  Dataset,
  DatasetProfile,
  WidgetInput,
} from "../../../data/types";
import { useSession } from "../../../session/SessionContext";
import { useCreateWidget } from "../../dashboards/hooks";

import { defaults, queryCaption } from "../chartConfig";
import { nextWidgetLayout } from "../widgetPlacement";
import { ChartTypePicker } from "./ChartTypePicker";
import { QueryFields } from "./QueryFields";

type NewChartFormProps = {
  dashboard: Dashboard;
  dataset: Dataset;
  profile: DatasetProfile;
  onClose: () => void;
  onCreated: (id: string) => void;
  onBusyChange: (busy: boolean) => void;
};

export function NewChartForm({
  dashboard,
  dataset,
  profile,
  onClose,
  onCreated,
  onBusyChange,
}: NewChartFormProps) {
  const { adapter } = useSession();
  const createWidget = useCreateWidget(dashboard.projectId);

  const [input, setInput] = useState<WidgetInput>({
    datasetId: dataset.id,
    ...defaults(profile),
    type: "BarChart",
    title: "",
  });

  const create = useMutation({
    mutationFn: async () => {
      const data = await adapter.query(dashboard.projectId, dataset.id, input);

      if (!data.length) {
        throw new Error(
          "This question returned no values. Try another measure.",
        );
      }

      const existingWidgets = await adapter.listWidgets(
        dashboard.projectId,
        dashboard.id,
      );

      return createWidget.mutateAsync({
        dashboardId: dashboard.id,
        input: {
          ...input,
          title: input.title.trim() || queryCaption(input),
          layout: nextWidgetLayout(existingWidgets),
        },
      });
    },
    onSuccess: (widget) => onCreated(widget.id),
    onSettled: () => onBusyChange(false),
  });

  return (
    <form
      className="analytics-dialog"
      onSubmit={(event) => {
        event.preventDefault();
        onBusyChange(true);
        create.mutate();
      }}
    >
      <ChartTypePicker
        value={input.type}
        onChange={(type) => setInput({ ...input, type })}
      />

      <div className="new-chart-fields">
        <QueryFields
          profile={profile}
          config={input}
          onChange={(value) => setInput({ ...input, ...value })}
        />
      </div>

      <TextInput
        label="Chart title"
        placeholder={queryCaption(input)}
        value={input.title}
        maxLength={200}
        onChange={(event) =>
          setInput({ ...input, title: event.currentTarget.value })
        }
      />

      <div className="query-sentence">
        <Icon name="chart" size={18} />
        <span>{queryCaption(input)}</span>
      </div>

      {create.isError && <ErrorState error={create.error} />}

      <div className="analytics-dialog-actions">
        <Button
          type="button"
          variant="default"
          disabled={create.isPending}
          onClick={onClose}
        >
          Cancel
        </Button>

        <Button
          type="submit"
          loading={create.isPending}
          disabled={!input.measureColumn || !input.groupByColumn}
        >
          Add to canvas
        </Button>
      </div>
    </form>
  );
}
