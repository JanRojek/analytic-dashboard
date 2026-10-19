import type { Widget } from "../../data/types";
import type { EditorDocument } from "../../mocks/editor";

export function orderedWidgets(widgets: Widget[], document: EditorDocument) {
  const ids = [
    ...document.order.filter((id) =>
      widgets.some((widget) => widget.id === id),
    ),
    ...widgets
      .filter((widget) => !document.order.includes(widget.id))
      .map((widget) => widget.id),
  ];

  return ids.map((id) => ({
    ...widgets.find((widget) => widget.id === id)!,
    ...document.overrides[id],
  }));
}
