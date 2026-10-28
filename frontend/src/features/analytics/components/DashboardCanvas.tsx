import {
  Children,
  cloneElement,
  isValidElement,
  useEffect,
  useRef,
  useState,
  type ReactElement,
  type ReactNode,
} from "react";

import { transformStrategy } from "react-grid-layout/core";
import {
  DASHBOARD_GRID_COLUMNS,
  DASHBOARD_GRID_ROW_HEIGHT,
  DASHBOARD_GRID_GAP,
  DASHBOARD_GRID_PADDING,
  resolveGridGeometry,
  type DashboardGridGeometry,
} from "../../../data/dashboardGrid";

import ReactGridLayout, {
  getCompactor,
  useContainerWidth,
  type Layout,
  type LayoutItem,
} from "react-grid-layout";

import "react-grid-layout/css/styles.css";
import "react-resizable/css/styles.css";

const MIN_WIDTH = 6;
const MIN_HEIGHT = 14;
const INTERACTION_CLASS = "aperture-canvas-interacting";
const INVALID_CLASS = "aperture-placement-invalid";

const dragCompactor = getCompactor(null, true, false);
const resizeCompactor = getCompactor(null, false, true);

type DashboardCanvasProps = {
  editing: boolean;
  children: ReactNode;
  geometry: DashboardGridGeometry;
  onGeometryCommit?: (geometry: DashboardGridGeometry) => void;
};

function stopTextSelectionLock() {
  document.body.classList.remove(INTERACTION_CLASS);
}

function startTextSelectionLock() {
  document.body.classList.add(INTERACTION_CLASS);
}

function toGeometry(layout: Layout): DashboardGridGeometry {
  return Object.fromEntries(
      layout.map(({ i, x, y, w, h }) => [i, { x, y, w, h }]),
  );
}

function overlap(a: LayoutItem, b: LayoutItem): boolean {
  if (a.i === b.i) return false;

  return (
      a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y
  );
}

function hasCollision(layout: Layout, activeId?: string): boolean {
  const candidates = activeId
      ? layout.filter((item) => item.i === activeId)
      : layout;

  return candidates.some((item) =>
      layout.some((other) => overlap(item, other)),
  );
}

function reconcileLayout(geometry: DashboardGridGeometry, ids: string[]): Layout {
  const resolved = resolveGridGeometry(geometry, ids);

  return ids.map((id): LayoutItem => ({
    i: id,
    ...resolved[id],
    minW: MIN_WIDTH,
    minH: MIN_HEIGHT,
  }));
}

export function DashboardCanvas({
  editing,
  children,
  geometry,
  onGeometryCommit,
}: DashboardCanvasProps) {
  const { containerRef, width, mounted } = useContainerWidth();
  const [isResizing, setIsResizing] = useState(false);
  const [gridRevision, setGridRevision] = useState(0);
  const activeWidgetId = useRef<string | undefined>(undefined);

  useEffect(() => {
    window.addEventListener("mouseup", stopTextSelectionLock);
    window.addEventListener("touchend", stopTextSelectionLock);
    window.addEventListener("blur", stopTextSelectionLock);

    return () => {
      window.removeEventListener("mouseup", stopTextSelectionLock);
      window.removeEventListener("touchend", stopTextSelectionLock);
      window.removeEventListener("blur", stopTextSelectionLock);
      stopTextSelectionLock();
    };
  }, []);

  // React.Children.toArray normalizes keys. Use the stable widget IDs instead,
  // so coordinates survive remounts and the draft -> widget transition.
  const items = Children.toArray(children)
      .filter(
          (child): child is ReactElement<{ "data-layout-id"?: string }> =>
              isValidElement(child),
      )
      .map((child) =>
          cloneElement(child, {
            key: child.props["data-layout-id"] ?? child.key,
          }),
      );
  const ids = items.map((child) => String(child.key));
  const layout = reconcileLayout(geometry, ids);

  function startInteraction(item: LayoutItem | null) {
    activeWidgetId.current = item?.i;
    startTextSelectionLock();
  }

  function finishInteraction(nextLayout: Layout) {
    stopTextSelectionLock();
    activeWidgetId.current = undefined;

    if (hasCollision(nextLayout)) {
      // Discard the invalid transient layout by remounting from saved geometry.
      setGridRevision((revision) => revision + 1);
    } else {
      onGeometryCommit?.(toGeometry(nextLayout));
    }
  }

  return (
      <div className="dashboard-canvas" ref={containerRef}>
        {mounted && (
            <ReactGridLayout
                key={gridRevision}
                width={width}
                layout={layout}
                gridConfig={{
                  cols: DASHBOARD_GRID_COLUMNS,
                  rowHeight: DASHBOARD_GRID_ROW_HEIGHT,
                  margin: [DASHBOARD_GRID_GAP, DASHBOARD_GRID_GAP],
                  containerPadding: [DASHBOARD_GRID_PADDING, DASHBOARD_GRID_PADDING],
                }}
                positionStrategy={transformStrategy}
                dragConfig={{
                  enabled: editing,
                  bounded: false,
                  threshold: 3,
                  cancel:
                      'button, a, input, select, textarea, [contenteditable="true"], .react-resizable-handle',
                }}
                resizeConfig={{
                  enabled: editing,
                  handles: ["n", "e", "s", "w", "nw", "ne", "sw", "se"],
                }}
                compactor={isResizing ? resizeCompactor : dragCompactor}
                onDragStart={(_currentLayout, _oldItem, item) => {
                  if (editing) startInteraction(item);
                }}
                onDrag={(nextLayout, _oldItem, item, _placeholder, _event, node) => {
                  if (!editing) return;
                  const id = item?.i ?? activeWidgetId.current;
                  node?.classList.toggle(INVALID_CLASS, hasCollision(nextLayout, id));
                }}
                onDragStop={(
                    nextLayout,
                    _oldItem,
                    _item,
                    _placeholder,
                    _event,
                    node,
                ) => {
                  node?.classList.remove(INVALID_CLASS);
                  if (editing) finishInteraction(nextLayout);
                }}
                onResizeStart={(_currentLayout, _oldItem, item) => {
                  if (!editing) return;
                  startInteraction(item);
                  setIsResizing(true);
                }}
                onResizeStop={(nextLayout) => {
                  if (editing) finishInteraction(nextLayout);
                  if (editing) setIsResizing(false);
                }}
            >
              {items}
            </ReactGridLayout>
        )}
      </div>
  );
}
