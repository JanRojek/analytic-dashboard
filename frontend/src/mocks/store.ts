import { createSeed } from "./seed.ts";
import type { DemoState, StoredDataset } from "./seed.ts";

export const DEMO_STORAGE_KEY = "aperture.demo.v1";
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export function readDemoState(storage: StorageLike = localStorage): DemoState {
  let saved: string | null;
  try {
    saved = storage.getItem(DEMO_STORAGE_KEY);
  } catch {
    throw new Error(
      "Browser storage is unavailable. Allow site storage to open the demo workspace.",
    );
  }
  if (!saved) return createSeed();
  try {
    const state = JSON.parse(saved) as DemoState;
    if (
      state.version !== 1 ||
      !Array.isArray(state.projects) ||
      !Array.isArray(state.datasets) ||
      !Array.isArray(state.widgets) ||
      !Array.isArray(state.dashboards) ||
      !state.recipes
    )
      throw new Error("Invalid workspace");
    if (
      state.datasets.some(
        (item) =>
          !item.dataset?.id ||
          !Array.isArray(item.rows) ||
          !Array.isArray(item.columns),
      )
    )
      throw new Error("Invalid datasets");
    return state;
  } catch {
    throw new Error(
      "Saved demo data could not be read. Clear this site’s demo storage to start a fresh workspace.",
    );
  }
}

/** Commit only after serialization and persistence succeed; a failed save never changes the old workspace. */
export function mutateDemoState<T>(
  change: (state: DemoState) => T,
  storage: StorageLike = localStorage,
): T {
  const next = readDemoState(storage);
  const result = change(next);
  try {
    storage.setItem(DEMO_STORAGE_KEY, JSON.stringify(next));
  } catch {
    throw new Error(
      "Your browser could not save this change. Storage may be full or disabled; your previous workspace is unchanged.",
    );
  }
  return result;
}

export function requireProject(state: DemoState, projectId: string) {
  const project = state.projects.find((item) => item.id === projectId);
  if (!project)
    throw new Error(
      "This project was not found. Return to Projects to choose a workspace.",
    );
  return project;
}

export function requireDataset(
  state: DemoState,
  projectId: string,
  datasetId: string,
): StoredDataset {
  requireProject(state, projectId);
  const found = state.datasets.find(
    (item) => item.projectId === projectId && item.dataset.id === datasetId,
  );
  if (!found) throw new Error("This dataset was not found in the project.");
  return found;
}

export function requireDashboard(
  state: DemoState,
  projectId: string,
  dashboardId: string,
) {
  requireProject(state, projectId);
  const dashboard = state.dashboards.find(
    (item) => item.projectId === projectId && item.id === dashboardId,
  );
  if (!dashboard)
    throw new Error("This dashboard was not found in the project.");
  return dashboard;
}

export function normalizedName(name: string, maximum = 100): string {
  const normalized = name.trim();
  if (!normalized) throw new Error("Enter a name to continue.");
  if ([...normalized].length > maximum)
    throw new Error(`Keep the name within ${maximum} characters.`);
  return normalized;
}
