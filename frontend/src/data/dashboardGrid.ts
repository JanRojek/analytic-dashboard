import type { WidgetLayout } from "./types";

/** Geometry coordinates are expressed in grid cells, never pixels. */
export type DashboardGridCell = {
  x: number;
  y: number;
  w: number;
  h: number;
};

export type DashboardGridGeometry = Record<string, DashboardGridCell>;

/** v2 editor geometry. Persist the column count alongside the layout. */
export const DASHBOARD_GRID_COLUMNS = 24;
export const DASHBOARD_GRID_LEGACY_COLUMNS = 12;
export const DASHBOARD_GRID_DEFAULT_WIDTH = 12;
export const DASHBOARD_GRID_DEFAULT_HEIGHT = 20;

/**
 * Former row pitch: 22px row + 12px gap = 34px.
 * New row pitch: 5px row + 12px gap = 17px.
 * Doubling y and h therefore preserves rendered vertical positions and sizes.
 */
export const DASHBOARD_GRID_ROW_HEIGHT = 5;
export const DASHBOARD_GRID_GAP = 12;

/** .dashboard-paper's 12px CSS padding handles the outer insets. */
export const DASHBOARD_GRID_PADDING = 0;

/** Upgrade old 12-column layouts once; never scale already-upgraded layouts. */
export function upgradeLegacyGridGeometry(
  geometry: DashboardGridGeometry,
): DashboardGridGeometry {
  return Object.fromEntries(
    Object.entries(geometry).map(([id, { x, y, w, h }]) => [
      id,
      { x: x * 2, y: y * 2, w: w * 2, h: h * 2 },
    ]),
  );
}


export function toGridCell(layout: WidgetLayout): DashboardGridCell {
  return {
    x: layout.x,
    y: layout.y,
    w: layout.width,
    h: layout.height,
  };
}

export function toWidgetLayout(cell: DashboardGridCell): WidgetLayout {
  return {
    x: cell.x,
    y: cell.y,
    width: cell.w,
    height: cell.h,
  };
}

export function resolveGridGeometry(
    geometry: DashboardGridGeometry,
    ids: readonly string[],
): DashboardGridGeometry {
  const resolved: DashboardGridGeometry = {};
  const occupied: DashboardGridCell[] = [];

  for (const id of ids) {
    const cell = geometry[id];

    if (!cell) continue;

    resolved[id] = cell;
    occupied.push(cell);
  }

  for (const id of ids) {
    if (resolved[id]) continue;

    const cell = findFirstFreeGridCell(occupied);

    resolved[id] = cell;
    occupied.push(cell);
  }

  return resolved;
}

export function findFirstFreeGridCell(
    occupied: readonly DashboardGridCell[],
    width = DASHBOARD_GRID_DEFAULT_WIDTH,
    height = DASHBOARD_GRID_DEFAULT_HEIGHT,
): DashboardGridCell {
  const byX = [...occupied].sort((a, b) => a.x - b.x);

  const candidateRows = [
    ...new Set([0, ...occupied.map((cell) => cell.y + cell.h)]),
  ].sort((a, b) => a - b);

  for (const y of candidateRows) {
    let x = 0;

    for (const cell of byX) {
      const overlapsVertically =
          y < cell.y + cell.h && cell.y < y + height;

      if (!overlapsVertically) continue;

      if (x + width <= cell.x) break;

      x = Math.max(x, cell.x + cell.w);

      if (x + width > DASHBOARD_GRID_COLUMNS) break;
    }

    if (x + width <= DASHBOARD_GRID_COLUMNS) {
      return { x, y, w: width, h: height };
    }
  }

  throw new Error("Could not find space for the widget.");
}
