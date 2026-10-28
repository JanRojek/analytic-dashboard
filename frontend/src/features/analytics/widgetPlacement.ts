import type {Widget, WidgetLayout} from "../../data/types.ts";

import {resolveGridGeometry, toGridCell, toWidgetLayout} from "../../data/dashboardGrid.ts";

export function nextWidgetLayout(
    widgets: readonly Widget[],
): WidgetLayout {
  const geometry = Object.fromEntries(
      widgets
          .filter((widget) => Boolean(widget.layout))
          .map((widget) => [
            widget.id,
            toGridCell(widget.layout),
          ]),
  );

  const newId = "__new-widget__";

  const resolved = resolveGridGeometry(
      geometry,
      [...widgets.map((widget) => widget.id), newId],
  );

  return toWidgetLayout(resolved[newId]);
}
