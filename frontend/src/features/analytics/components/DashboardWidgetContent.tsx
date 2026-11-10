import { memo } from "react";
import { ActionIcon, Tooltip } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type { Widget } from "../../../data/types";
import { queryCaption } from "../chartConfig";
import { WidgetChart } from "./WidgetChart";

type DashboardWidgetContentProps = {
  widget: Widget;
  projectId: string;
  editing: boolean;
  selected: boolean;
  onSelect: (widgetId: string) => void;
  onInspectResults: (widgetId: string) => void;
};

export const DashboardWidgetContent = memo(function DashboardWidgetContent({
  widget,
  projectId,
  editing,
  selected,
  onSelect,
  onInspectResults,
}: DashboardWidgetContentProps) {
  return (
    <>
      <div className="canvas-chart-heading">
        <div>
          <h2>{widget.title}</h2>
          <p>{queryCaption(widget)}</p>
        </div>

        {editing ? (
          <Tooltip label={`Configure ${widget.title}`}>
            <ActionIcon
              variant={selected ? "light" : "subtle"}
              aria-label={`Configure ${widget.title}`}
              onClick={() => onSelect(widget.id)}
            >
              <Icon name="settings" size={16} />
            </ActionIcon>
          </Tooltip>
        ) : (
          <Tooltip label="Inspect results">
            <ActionIcon
              variant="subtle"
              aria-label={`Inspect results for ${widget.title}`}
              onClick={() => onInspectResults(widget.id)}
            >
              <Icon name="data" size={16} />
            </ActionIcon>
          </Tooltip>
        )}
      </div>

      <WidgetChart widget={widget} projectId={projectId} fillHeight />
    </>
  );
});
