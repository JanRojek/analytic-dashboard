import type { EditorSnapshot } from "./editorState";

interface StoredSnapshotDraft {
  version: 1;
  baseRevision: number;
  snapshot: EditorSnapshot;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isWidget(value: unknown): boolean {
  if (!isRecord(value) || !isRecord(value.layout)) return false;

  const layout = value.layout;
  return (
    [
      value.id,
      value.dashboardId,
      value.datasetId,
      value.type,
      value.title,
      value.groupByColumn,
      value.measureColumn,
      value.aggregation,
      value.createdAtUtc,
    ].every((item) => typeof item === "string") &&
    [layout.x, layout.y, layout.width, layout.height].every((item) =>
      Number.isSafeInteger(item),
    ) &&
    (layout.x as number) >= 0 &&
    (layout.y as number) >= 0 &&
    (layout.width as number) > 0 &&
    (layout.height as number) > 0 &&
    (layout.x as number) + (layout.width as number) <= 24
  );
}

export function readEditorSnapshotDraft(
  key: string,
): Omit<StoredSnapshotDraft, "version"> | null {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return null;

    const value: unknown = JSON.parse(raw);
    if (
      !isRecord(value) ||
      value.version !== 1 ||
      !Number.isSafeInteger(value.baseRevision) ||
      (value.baseRevision as number) < 0 ||
      !isRecord(value.snapshot) ||
      !Array.isArray(value.snapshot.widgets) ||
      !value.snapshot.widgets.every(isWidget)
    ) {
      return null;
    }

    const snapshot = value.snapshot as unknown as EditorSnapshot;
    const ids = snapshot.widgets.map((widget) => widget.id);
    if (new Set(ids).size !== ids.length) return null;

    return {
      baseRevision: value.baseRevision as number,
      snapshot,
    };
  } catch {
    return null;
  }
}

export function persistEditorSnapshotDraft(
  key: string,
  snapshot: EditorSnapshot,
  baseRevision: number,
): void {
  const record: StoredSnapshotDraft = {
    version: 1,
    baseRevision,
    snapshot,
  };
  localStorage.setItem(key, JSON.stringify(record));
}

export function clearEditorSnapshotDraft(key: string): void {
  localStorage.removeItem(key);
}
