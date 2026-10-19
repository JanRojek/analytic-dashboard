import { Chart } from "../../../components/Charts";
import { ErrorState } from "../../../components/Feedback";
import type { Widget } from "../../../data/types";

import { useChartData } from "../hooks";

export function WidgetChart({
  widget,
  projectId,
  compact = false,
}: {
  widget: Widget;
  projectId: string;
  compact?: boolean;
}) {
  const config = {
    groupByColumn: widget.groupByColumn,
    measureColumn: widget.measureColumn,
    aggregation: widget.aggregation,
  };

  const data = useChartData(projectId, widget.datasetId, config);

  if (data.isPending)
    return (
      <div className="widget-loading" role="status">
        Loading chart…
      </div>
    );

  if (data.isError)
    return compact ? (
      <div className="widget-loading">Chart unavailable</div>
    ) : (
      <ErrorState error={data.error} retry={() => data.refetch()} />
    );

  return (
    <Chart
      type={widget.type}
      items={data.data}
      title={widget.title}
      compact={compact}
      aggregation={widget.aggregation}
    />
  );
}
