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

  const results = useChartData(projectId, widget.datasetId, config);

  return results.isPending ? (
    <LoadingState />
  ) : results.isError ? (
    <ErrorState error={results.error} />
  ) : (
    <ResultsTable items={results.data} config={config} />
  );
}
