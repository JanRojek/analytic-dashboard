import {
  resolveGridGeometry,
  toWidgetLayout,
} from "../../../data/dashboardGrid";
import type { DashboardWidgetUpdate, Widget } from "../../../data/types";
import type { EditorDocument } from "../../../mocks/editor";

import { orderedWidgets } from "../dashboardUtils";
import type { EditorSnapshot } from "./editorState";

export function createEditorSnapshot(
  sourceWidgets: Widget[],
  document: EditorDocument,
): EditorSnapshot {
  const widgets = orderedWidgets(sourceWidgets, document);

  const geometry = resolveGridGeometry(
    document.gridLayout ?? {},
    widgets.map((widget) => widget.id),
  );

  return {
    widgets: widgets.map((widget) => ({
      ...widget,
      layout: toWidgetLayout(geometry[widget.id]),
    })),
  };
}

export function toDashboardWidgetUpdates(
  snapshot: EditorSnapshot,
): DashboardWidgetUpdate[] {
  return snapshot.widgets.map((widget) => ({
    widgetId: widget.id,
    datasetId: widget.datasetId,
    type: widget.type,
    title: widget.title,
    groupByColumn: widget.groupByColumn,
    measureColumn: widget.measureColumn,
    aggregation: widget.aggregation,
    layout: { ...widget.layout },
  }));
}
