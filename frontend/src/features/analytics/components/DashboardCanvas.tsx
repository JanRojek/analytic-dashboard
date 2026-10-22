import {
  Children,
  isValidElement,
  useEffect,
  useRef,
  useState,
  type ReactElement,
  type ReactNode,
} from "react";

import { transformStrategy } from "react-grid-layout/core";

import ReactGridLayout, {
  getCompactor,
  useContainerWidth,
  type Layout,
  type LayoutItem,
} from "react-grid-layout";

import "react-grid-layout/css/styles.css";
import "react-resizable/css/styles.css";

const COLUMNS = 12;
const DEFAULT_WIDTH = 6;
const DEFAULT_HEIGHT = 10;
const INTERACTION_CLASS = "aperture-canvas-interacting";
const INVALID_CLASS = "aperture-placement-invalid";

const dragCompactor = getCompactor(null, true, false);
const resizeCompactor = getCompactor(null, false, true);

type DashboardCanvasProps = {
  editing: boolean;
  children: ReactNode;
};

function stopTextSelectionLock() {
  document.body.classList.remove(INTERACTION_CLASS);
}

function startTextSelectionLock() {
  document.body.classList.add(INTERACTION_CLASS);
}

function copyLayout(layout: Layout): Layout {
  return layout.map((item) => ({ ...item }));
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

function reconcileLayout(previous: Layout, ids: string[]): Layout {
  const idSet = new Set(ids);
  const existing = previous.filter((item) => idSet.has(item.i));
  const byId = new Map(existing.map((item) => [item.i, item]));

  // New charts go below existing charts; deleting an item never fills gaps.
  const nextRow = Math.max(0, ...existing.map((item) => item.y + item.h));
  let added = 0;

  return ids.map((id): LayoutItem => {
    const known = byId.get(id);
    if (known) return known;

    const index = added++;
    return {
      i: id,
      x: (index % 2) * DEFAULT_WIDTH,
      y: nextRow + Math.floor(index / 2) * DEFAULT_HEIGHT,
      w: DEFAULT_WIDTH,
      h: DEFAULT_HEIGHT,
      minW: 3,
      minH: 7,
    };
  });
}

export function DashboardCanvas({ editing, children }: DashboardCanvasProps) {
  const { containerRef, width, mounted } = useContainerWidth();
  const [committedLayout, setCommittedLayout] = useState<Layout>([]);
  const [isResizing, setIsResizing] = useState(false);
  const [gridRevision, setGridRevision] = useState(0);
  const beforeInteraction = useRef<Layout>([]);
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

  const items = Children.toArray(children).filter(
    (child): child is ReactElement => isValidElement(child),
  );
  const ids = items.map((child) => String(child.key));
  const layout = reconcileLayout(committedLayout, ids);

  function startInteraction(currentLayout: Layout, item: LayoutItem | null) {
    beforeInteraction.current = copyLayout(currentLayout);
    activeWidgetId.current = item?.i;
    startTextSelectionLock();
  }

  function finishInteraction(nextLayout: Layout) {
    stopTextSelectionLock();
    activeWidgetId.current = undefined;

    if (hasCollision(nextLayout)) {
      // RGL keeps transient positions internally. Remount only after a rejected
      // gesture to make it restore the last valid layout unambiguously.
      setCommittedLayout(copyLayout(beforeInteraction.current));
      setGridRevision((revision) => revision + 1);
    } else {
      setCommittedLayout(copyLayout(nextLayout));
    }
  }

  // Presentation still uses the old grid while we validate editor UX.
  if (!editing) {
    return <div className="canvas-chart-grid">{children}</div>;
  }

  return (
    <div className="dashboard-canvas" ref={containerRef}>
      {mounted && (
        <ReactGridLayout
          key={gridRevision}
          width={width}
          layout={layout}
          gridConfig={{ cols: COLUMNS, rowHeight: 22, margin: [12, 12] }}
          positionStrategy={transformStrategy}
          dragConfig={{
            enabled: true,
            bounded: false,
            threshold: 3,
            cancel:
              'button, a, input, select, textarea, [contenteditable="true"], .react-resizable-handle',
          }}
          resizeConfig={{
            enabled: true,
            handles: ["n", "e", "s", "w", "nw", "ne", "sw", "se"],
          }}
          compactor={isResizing ? resizeCompactor : dragCompactor}
          onDragStart={(currentLayout, _oldItem, item) => {
            startInteraction(currentLayout, item);
          }}
          onDrag={(nextLayout, _oldItem, item, _placeholder, _event, node) => {
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
            finishInteraction(nextLayout);
          }}
          onResizeStart={(currentLayout, _oldItem, item) => {
            startInteraction(currentLayout, item);
            setIsResizing(true);
          }}
          onResizeStop={(nextLayout) => {
            finishInteraction(nextLayout);
            setIsResizing(false);
          }}
        >
          {items}
        </ReactGridLayout>
      )}
    </div>
  );
}
