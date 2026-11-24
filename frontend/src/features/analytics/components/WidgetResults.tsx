import { ErrorState, LoadingState } from "../../../components/Feedback";

import type { Widget } from "../../../data/types";

import { useChartData } from "../hooks";
import { ResultsTable } from "./ResultsTable";

export function WidgetResults({
  widget,
  projectId,
}: {
  widget: Widget;
  projectId: string;
}) {
  const config = {
    groupByColumn: widget.groupByColumn,
    measureColumn: widget.measureColumn,
    aggregation: widget.aggregation,
  };

  const results = useChartData(
    projectId,
    widget.datasetId ?? "",
    config,
    widget.datasetId !== null,
  );

  if (widget.datasetId === null) {
    return (
      <p className="analytics-note">
        This chart's source dataset was removed. Its configuration is preserved.
      </p>
    );
  }

  return results.isPending ? (
    <LoadingState />
  ) : results.isError ? (
    <ErrorState error={results.error} />
  ) : (
    <ResultsTable items={results.data} config={config} />
  );
}
