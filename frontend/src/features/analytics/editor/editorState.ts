import type { Widget, WidgetInput, WidgetLayout } from "../../../data/types";

export interface EditorSnapshot {
  widgets: Widget[];
}

export function addWidget(
  snapshot: EditorSnapshot,
  widget: Widget,
): EditorSnapshot {
  if (snapshot.widgets.some((item) => item.id === widget.id)) {
    throw new Error("A widget with this ID already exists.");
  }

  return {
    ...snapshot,
    widgets: [
      ...snapshot.widgets,
      {
        ...widget,
        layout: { ...widget.layout },
      },
    ],
  };
}

export function updateWidget(
  snapshot: EditorSnapshot,
  widgetId: string,
  patch: Partial<WidgetInput>,
): EditorSnapshot {
  const widgetExists = snapshot.widgets.some(
    (widget) => widget.id === widgetId,
  );

  if (!widgetExists) {
    return snapshot;
  }

  return {
    ...snapshot,
    widgets: snapshot.widgets.map((widget) =>
      widget.id === widgetId ? { ...widget, ...patch } : widget,
    ),
  };
}

export function updateWidgetLayouts(
  snapshot: EditorSnapshot,
  layouts: Record<string, WidgetLayout>,
): EditorSnapshot {
  let changed = false;

  const widgets = snapshot.widgets.map((widget) => {
    const layout = layouts[widget.id];

    if (!layout) {
      return widget;
    }

    const current = widget.layout;

    if (
      current.x === layout.x &&
      current.y === layout.y &&
      current.width === layout.width &&
      current.height === layout.height
    ) {
      return widget;
    }

    changed = true;

    return {
      ...widget,
      layout: { ...layout },
    };
  });

  if (!changed) {
    return snapshot;
  }

  return {
    ...snapshot,
    widgets,
  };
}

export function removeWidget(
  snapshot: EditorSnapshot,
  widgetId: string,
): EditorSnapshot {
  if (!snapshot.widgets.some((widget) => widget.id === widgetId)) {
    return snapshot;
  }

  return {
    ...snapshot,
    widgets: snapshot.widgets.filter((widget) => widget.id !== widgetId),
  };
}
