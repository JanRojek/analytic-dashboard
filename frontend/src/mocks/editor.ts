import type { DataMode, QueryConfig, WidgetInput } from "../data/types";

// The API can create widgets, but cannot update their configuration or layout yet.
// These browser-only overrides are deliberately kept out of the API adapter.
export interface EditorDocument {
  order: string[];
  widths: Record<string, "half" | "full">;
  overrides: Record<string, Partial<WidgetInput>>;
  savedAt?: string;
}
export interface ExplorationDraft extends QueryConfig {
  datasetId: string;
  type: WidgetInput["type"];
  title: string;
}
export function editorScope(mode: DataMode, userId: string, projectId: string) {
  return `aperture:${mode}:${userId}:${projectId}`;
}
const emptyDocument = (): EditorDocument => ({
  order: [],
  widths: {},
  overrides: {},
});
function readStored(key: string): unknown {
  try {
    return JSON.parse(localStorage.getItem(key) || "null") as unknown;
  } catch {
    return null;
  }
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
const widgetTypes = new Set(["Kpi", "BarChart", "LineChart", "PieChart"]);
const aggregations = new Set(["Sum", "Average", "Min", "Max", "Count"]);
function parseDocument(value: unknown): EditorDocument | null {
  if (
    !isRecord(value) ||
    !Array.isArray(value.order) ||
    !value.order.every((id) => typeof id === "string") ||
    !isRecord(value.widths) ||
    !isRecord(value.overrides)
  )
    return null;
  if (
    Object.values(value.widths).some(
      (width) => width !== "half" && width !== "full",
    )
  )
    return null;
  const overrides: EditorDocument["overrides"] = {};
  for (const [id, patch] of Object.entries(value.overrides)) {
    if (!isRecord(patch)) return null;
    const fields = [
      "title",
      "datasetId",
      "groupByColumn",
      "measureColumn",
      "type",
      "aggregation",
    ] as const;
    if (
      fields.some(
        (field) =>
          patch[field] !== undefined && typeof patch[field] !== "string",
      )
    )
      return null;
    if (patch.type !== undefined && !widgetTypes.has(patch.type as string))
      return null;
    if (
      patch.aggregation !== undefined &&
      !aggregations.has(patch.aggregation as string)
    )
      return null;
    overrides[id] = Object.fromEntries(
      fields
        .filter((field) => patch[field] !== undefined)
        .map((field) => [field, patch[field]]),
    );
  }
  return {
    order: [...new Set(value.order)],
    widths: value.widths as EditorDocument["widths"],
    overrides,
    ...(typeof value.savedAt === "string" ? { savedAt: value.savedAt } : {}),
  };
}
export function readEditor(
  scope: string,
  dashboardId: string,
  includeDraft = true,
): { document: EditorDocument; dirty: boolean } {
  const draft = includeDraft
    ? parseDocument(readStored(`${scope}:editor:${dashboardId}:draft`))
    : null;
  const saved = parseDocument(readStored(`${scope}:editor:${dashboardId}`));
  return { document: draft || saved || emptyDocument(), dirty: Boolean(draft) };
}
export function persistEditorDraft(
  scope: string,
  dashboardId: string,
  document: EditorDocument,
) {
  localStorage.setItem(
    `${scope}:editor:${dashboardId}:draft`,
    JSON.stringify(document),
  );
}
export function saveEditor(
  scope: string,
  dashboardId: string,
  document: EditorDocument,
) {
  const saved = { ...document, savedAt: new Date().toISOString() };
  localStorage.setItem(`${scope}:editor:${dashboardId}`, JSON.stringify(saved));
  localStorage.removeItem(`${scope}:editor:${dashboardId}:draft`);
  return saved;
}
export function removeEditor(scope: string, dashboardId: string) {
  localStorage.removeItem(`${scope}:editor:${dashboardId}`);
  localStorage.removeItem(`${scope}:editor:${dashboardId}:draft`);
}
export function readExploration(scope: string): ExplorationDraft | null {
  const value = readStored(`${scope}:exploration`);
  if (
    !isRecord(value) ||
    ![
      "datasetId",
      "groupByColumn",
      "measureColumn",
      "aggregation",
      "type",
      "title",
    ].every((field) => typeof value[field] === "string")
  )
    return null;
  if (
    !widgetTypes.has(value.type as string) ||
    !aggregations.has(value.aggregation as string)
  )
    return null;
  return value as unknown as ExplorationDraft;
}
export function saveExploration(scope: string, draft: ExplorationDraft) {
  localStorage.setItem(`${scope}:exploration`, JSON.stringify(draft));
}
