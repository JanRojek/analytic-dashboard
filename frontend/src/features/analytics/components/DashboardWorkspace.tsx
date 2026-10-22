import { useEffect, useState } from "react";
import {
  ActionIcon,
  Button,
  Modal,
  SegmentedControl,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { Brand } from "../../../components/Brand";
import { EmptyState, ErrorState } from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import type {
  Dashboard,
  Widget,
  WidgetInput,
  WidgetType,
} from "../../../data/types";
import {
  editorScope,
  persistEditorDraft,
  readEditor,
  saveEditor,
  type EditorDocument,
} from "../../../mocks/editor";
import { useSession } from "../../../session/SessionContext";
import { useDeleteWidget } from "../../dashboards/hooks";

import { chartTypes, queryCaption } from "../chartConfig";
import { orderedWidgets } from "../dashboardUtils";
import { createDraftWidget, type DraftWidget } from "../draftWidgets";
import { DashboardCanvas } from "./DashboardCanvas";
import { ChartGallery } from "./ChartGallery";
import { ChartTypePicker } from "./ChartTypePicker";
import { DraftChartInspector } from "./DraftChartInspector";
import { InspectorQuery } from "./InspectorQuery";
import { WidgetChart } from "./WidgetChart";
import { WidgetResults } from "./WidgetResults";

type DashboardWorkspaceProps = {
  dashboard: Dashboard;
  sourceWidgets: Widget[];
  editing: boolean;
};

function readDraftWidgets(key: string): DraftWidget[] {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return [];

    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];

    // A damaged/old browser draft must not break the whole editor.
    return parsed.filter(
      (draft): draft is DraftWidget =>
        typeof draft === "object" &&
        draft !== null &&
        typeof draft.id === "string" &&
        typeof draft.title === "string" &&
        chartTypes.some((type) => type.value === draft.type),
    );
  } catch {
    return [];
  }
}

export function DashboardWorkspace({
  dashboard,
  sourceWidgets,
  editing,
}: DashboardWorkspaceProps) {
  const { mode, session } = useSession();
  const navigate = useNavigate();
  const scope = editorScope(mode, session?.user.id || "", dashboard.projectId);
  const draftKey = `aperture:widget-drafts:${scope}:${dashboard.id}`;

  const [initial] = useState(() => readEditor(scope, dashboard.id, editing));
  const [document, setDocument] = useState(initial.document);
  const [dirty, setDirty] = useState(initial.dirty);
  const [selectedId, setSelectedId] = useState(sourceWidgets[0]?.id || "");
  const [draftWidgets, setDraftWidgets] = useState<DraftWidget[]>(() =>
    readDraftWidgets(draftKey),
  );

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [showTable, setShowTable] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [saveMessage, setSaveMessage] = useState("");

  const widgets = orderedWidgets(sourceWidgets, document);
  const visibleDrafts = editing ? draftWidgets : [];
  const chartCount = widgets.length + visibleDrafts.length;

  const selectedDraft = draftWidgets.find((draft) => draft.id === selectedId);
  const selected = selectedDraft
    ? undefined
    : widgets.find((widget) => widget.id === selectedId) || widgets[0];

  const remove = useDeleteWidget(dashboard.projectId);

  useEffect(() => {
    if (!dirty || !editing) return;

    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, [dirty, editing]);

  useEffect(() => {
    if (!editing) return;

    try {
      localStorage.setItem(draftKey, JSON.stringify(draftWidgets));
    } catch {
      setSaveError("Your browser could not save chart drafts.");
    }
  }, [draftKey, draftWidgets, editing]);

  function update(next: EditorDocument) {
    setDocument(next);
    setDirty(true);
    setSaveMessage("");

    try {
      persistEditorDraft(scope, dashboard.id, next);
      setSaveError("");
    } catch {
      setSaveError(
        "Your browser could not save this draft. Free some browser storage before leaving.",
      );
    }
  }

  function persist(): boolean {
    if (
      widgets.some(
        (widget) =>
          !widget.title.trim() ||
          !widget.groupByColumn ||
          !widget.measureColumn,
      )
    ) {
      setSaveError(
        "Give every chart a title and choose its group and measure before saving.",
      );
      return false;
    }

    try {
      const saved = saveEditor(scope, dashboard.id, document);
      setDocument(saved);
      setDirty(false);
      setSaveError("");
      setSaveMessage("Saved in this browser");
      return true;
    } catch {
      setSaveError(
        "Your browser could not save these edits. Free some browser storage and try again.",
      );
      return false;
    }
  }

  function leave(path: string) {
    if (
      dirty &&
      !window.confirm(
        "Leave with unsaved layout changes? Your draft will stay in this browser, but the presentation shows your last saved layout.",
      )
    ) {
      return;
    }

    navigate(path);
  }

  function selectChart(id: string) {
    setSelectedId(id);
    setShowTable(false);
  }

  function addDraft(type: WidgetType) {
    const draft = createDraftWidget(type);
    setDraftWidgets((current) => [...current, draft]);
    selectChart(draft.id);
  }

  function changeDraft(id: string, patch: Partial<DraftWidget>) {
    setDraftWidgets((current) =>
      current.map((draft) =>
        draft.id === id ? { ...draft, ...patch } : draft,
      ),
    );
  }

  function discardDraft(id: string) {
    setDraftWidgets((current) => current.filter((draft) => draft.id !== id));
    selectChart(widgets[0]?.id || "");
  }

  function changeWidget(patch: Partial<WidgetInput>) {
    if (!selected) return;

    update({
      ...document,
      overrides: {
        ...document.overrides,
        [selected.id]: { ...document.overrides[selected.id], ...patch },
      },
    });
  }

  function move(direction: -1 | 1) {
    if (!selected) return;

    const ids = widgets.map((widget) => widget.id);
    const index = ids.indexOf(selected.id);
    const targetIndex = index + direction;

    if (targetIndex < 0 || targetIndex >= ids.length) return;

    [ids[index], ids[targetIndex]] = [ids[targetIndex], ids[index]];
    update({ ...document, order: ids });
  }

  const saveStatus = dirty
    ? "Layout draft kept locally"
    : draftWidgets.length > 0 && editing
      ? `${draftWidgets.length} chart draft${draftWidgets.length === 1 ? "" : "s"} kept locally`
      : saveMessage || "Saved in this browser";

  return (
    <div
      className={`dashboard-workspace ${editing ? "is-editing" : "is-viewing"}`}
    >
      <header className="canvas-header">
        <div
          className="canvas-header-start"
          onClickCapture={(event) => {
            if (
              dirty &&
              (event.target as HTMLElement).closest("a") &&
              !window.confirm(
                "Leave this editor? Your unsaved draft will remain in this browser.",
              )
            ) {
              event.preventDefault();
            }
          }}
        >
          <Brand />
          <span className="canvas-header-divider" />

          <button
            className="canvas-back"
            onClick={() => leave(`/projects/${dashboard.projectId}/dashboards`)}
          >
            <Icon name="back" size={16} />
            <span>Dashboards</span>
          </button>
        </div>

        <div className="canvas-document-name">
          <Icon name="dashboard" size={17} />
          <strong>{dashboard.name}</strong>
          <span className="canvas-mode-label">
            {editing ? "Editor" : "Presentation"}
          </span>
        </div>

        <div className="canvas-actions">
          {mode === "demo" && <span className="canvas-demo-label">Demo</span>}

          {editing ? (
            <>
              <span className="canvas-save-status" role="status">
                <span
                  className={
                    dirty ? "save-indicator unsaved" : "save-indicator"
                  }
                />
                {saveStatus}
              </span>

              <Tooltip label="View your saved dashboard">
                <Button
                  variant="default"
                  size="sm"
                  leftSection={<Icon name="eye" size={16} />}
                  onClick={() => {
                    if (!dirty || persist()) {
                      navigate(
                        `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/view`,
                      );
                    }
                  }}
                >
                  Preview
                </Button>
              </Tooltip>

              <Button
                size="sm"
                leftSection={<Icon name="check" size={16} />}
                onClick={persist}
              >
                Save changes
              </Button>
            </>
          ) : (
            <Button
              variant="default"
              leftSection={<Icon name="edit" size={16} />}
              onClick={() =>
                navigate(
                  `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/edit`,
                )
              }
            >
              Edit dashboard
            </Button>
          )}
        </div>
      </header>

      {saveError && (
        <div className="analytics-storage-error" role="alert">
          {saveError}
        </div>
      )}

      <div className="canvas-work-area">
        <main className="canvas-stage">
          <div className="canvas-size-label">
            {editing ? "DASHBOARD CANVAS" : "PROJECT DASHBOARD"}
            <span>
              {chartCount} {chartCount === 1 ? "chart" : "charts"}
            </span>
          </div>

          <div className="dashboard-paper">
            {!editing && (
              <div className="dashboard-paper-heading">
                <span className="eyebrow">AN OVERVIEW THAT MATTERS</span>
                <h1>{dashboard.name}</h1>
                <p>A clear view of your latest data.</p>

                <div className="dashboard-paper-meta">
                  <span>
                    <i className="status-dot" />
                    Current dataset versions
                  </span>
                  <span>
                    {mode === "demo" ? "Demo workspace" : "Personal workspace"}
                  </span>
                </div>
              </div>
            )}

            {chartCount > 0 ? (
              <DashboardCanvas editing={editing}>
                {widgets.map((widget) => (
                  <section
                    key={widget.id}
                    className={[
                      "canvas-chart",
                      document.widths[widget.id] === "full" && "full-width",
                      editing && selected?.id === widget.id && "is-selected",
                      widget.type === "Kpi" && "is-kpi",
                    ]
                      .filter(Boolean)
                      .join(" ")}
                    onClick={(event) => {
                      if (
                        editing &&
                        !(event.target as Element).closest(
                          "button, a, input, select",
                        )
                      ) {
                        selectChart(widget.id);
                      }
                    }}
                  >
                    <div className="canvas-chart-heading">
                      <div>
                        <h2>{widget.title}</h2>
                        <p>{queryCaption(widget)}</p>
                      </div>

                      {editing ? (
                        <Tooltip label={`Configure ${widget.title}`}>
                          <ActionIcon
                            variant={
                              selected?.id === widget.id ? "light" : "subtle"
                            }
                            aria-label={`Configure ${widget.title}`}
                            onClick={() => selectChart(widget.id)}
                          >
                            <Icon name="settings" size={16} />
                          </ActionIcon>
                        </Tooltip>
                      ) : (
                        <Tooltip label="Inspect results">
                          <ActionIcon
                            variant="subtle"
                            aria-label={`Inspect results for ${widget.title}`}
                            onClick={() => {
                              setSelectedId(widget.id);
                              setShowTable(true);
                            }}
                          >
                            <Icon name="data" size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                    </div>

                    <WidgetChart
                      widget={widget}
                      projectId={dashboard.projectId}
                      fillHeight={editing}
                    />
                  </section>
                ))}

                {visibleDrafts.map((draft) => {
                  const chartType = chartTypes.find(
                    (type) => type.value === draft.type,
                  );

                  return (
                    <section
                      key={draft.id}
                      className={`canvas-chart ${selectedDraft?.id === draft.id ? "is-selected" : ""}`}
                      role="button"
                      tabIndex={0}
                      aria-label={`Configure ${draft.title || chartType?.label || "new"} chart draft`}
                      onClick={() => selectChart(draft.id)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          selectChart(draft.id);
                        }
                      }}
                    >
                      <div className="canvas-chart-heading">
                        <div>
                          <h2>
                            {draft.title ||
                              `${chartType?.label || "New"} chart`}
                          </h2>
                          <p>Draft · not configured</p>
                        </div>
                      </div>

                      <div className="draft-chart-placeholder">
                        <Icon name={chartType?.icon || "chart"} size={28} />
                        <span>Configure this chart in the right panel.</span>
                      </div>
                    </section>
                  );
                })}
              </DashboardCanvas>
            ) : (
              <EmptyState
                icon="chart"
                title="What would you like to understand?"
                description={
                  editing
                    ? "Choose a visualization from the right panel to add it to your dashboard."
                    : "This dashboard does not have any charts yet."
                }
              />
            )}
          </div>

          {editing && (
            <p className="canvas-stage-hint">
              <Icon name="shield" size={14} />
              Chart positions and sizes are experimental and will reset on
              refresh. Other editor drafts stay in this browser.
            </p>
          )}
        </main>

        {editing && (
          <aside className="canvas-inspector">
            <ChartGallery onAdd={addDraft} />

            <div className="canvas-panel-heading">Chart properties</div>

            {selectedDraft ? (
              <DraftChartInspector
                key={selectedDraft.id}
                dashboard={dashboard}
                draft={selectedDraft}
                onChange={(patch) => changeDraft(selectedDraft.id, patch)}
                onDiscard={() => discardDraft(selectedDraft.id)}
                onCreated={(widgetId) => {
                  setDraftWidgets((current) =>
                    current.filter((draft) => draft.id !== selectedDraft.id),
                  );
                  selectChart(widgetId);
                }}
              />
            ) : selected ? (
              <>
                <div className="inspector-section">
                  <TextInput
                    label="Title"
                    value={selected.title}
                    maxLength={200}
                    onChange={(event) =>
                      changeWidget({ title: event.currentTarget.value })
                    }
                  />

                  <details className="inspector-visualization-details">
                    <summary>
                      Change visualization
                      <Icon name="chevron" size={14} />
                    </summary>
                    <div className="inspector-visualization-picker">
                      <ChartTypePicker
                        value={selected.type}
                        onChange={(type) => changeWidget({ type })}
                      />
                    </div>
                  </details>
                </div>

                <div className="inspector-section">
                  <span className="inspector-label">Layout</span>
                  <SegmentedControl
                    fullWidth
                    size="xs"
                    value={document.widths[selected.id] || "half"}
                    onChange={(value) =>
                      update({
                        ...document,
                        widths: {
                          ...document.widths,
                          [selected.id]: value as "half" | "full",
                        },
                      })
                    }
                    data={[
                      { value: "half", label: "Half width" },
                      { value: "full", label: "Full width" },
                    ]}
                  />

                  {widgets.length > 1 && (
                    <div className="inspector-reorder">
                      <span>Position</span>
                      <Button
                        variant="default"
                        size="compact-xs"
                        disabled={widgets[0]?.id === selected.id}
                        onClick={() => move(-1)}
                      >
                        Move earlier
                      </Button>
                      <Button
                        variant="default"
                        size="compact-xs"
                        disabled={
                          widgets[widgets.length - 1]?.id === selected.id
                        }
                        onClick={() => move(1)}
                      >
                        Move later
                      </Button>
                    </div>
                  )}
                </div>

                <InspectorQuery
                  key={selected.id}
                  widget={selected}
                  projectId={dashboard.projectId}
                  onChange={changeWidget}
                  onOpenDataset={() =>
                    leave(
                      `/projects/${dashboard.projectId}/data/${selected.datasetId}`,
                    )
                  }
                />

                <div className="inspector-section">
                  <Button
                    fullWidth
                    variant="default"
                    size="xs"
                    leftSection={<Icon name="data" size={15} />}
                    onClick={() => setShowTable(true)}
                  >
                    Inspect results
                  </Button>

                  <Button
                    fullWidth
                    variant="subtle"
                    color="red"
                    size="xs"
                    mt={8}
                    leftSection={<Icon name="trash" size={15} />}
                    onClick={() => {
                      remove.reset();
                      setDeleteOpen(true);
                    }}
                  >
                    Remove chart
                  </Button>
                </div>
              </>
            ) : (
              <div className="inspector-empty">
                <Icon name="settings" size={28} />
                <h3>A little room for possibility</h3>
                <p>Choose a visualization above to begin.</p>
              </div>
            )}
          </aside>
        )}
      </div>

      <Modal
        opened={deleteOpen}
        onClose={remove.isPending ? () => {} : () => setDeleteOpen(false)}
        title="Remove this chart?"
        centered
      >
        <div className="analytics-dialog">
          <p>
            <strong>{selected?.title}</strong> will be permanently removed from
            this dashboard. Your source dataset will stay available.
          </p>

          {remove.isError && <ErrorState error={remove.error} />}

          <div className="analytics-dialog-actions">
            <Button
              variant="default"
              onClick={() => setDeleteOpen(false)}
              disabled={remove.isPending}
            >
              Keep chart
            </Button>

            <Button
              color="red"
              loading={remove.isPending}
              onClick={() => {
                if (!selected) return;

                remove.mutate(
                  {
                    dashboardId: dashboard.id,
                    widgetId: selected.id,
                  },
                  {
                    onSuccess: () => setDeleteOpen(false),
                  },
                );
              }}
            >
              Remove chart
            </Button>
          </div>
        </div>
      </Modal>

      <Modal
        opened={showTable && Boolean(selected)}
        onClose={() => setShowTable(false)}
        title={selected?.title}
        size="lg"
        centered
      >
        {selected && (
          <WidgetResults widget={selected} projectId={dashboard.projectId} />
        )}
      </Modal>
    </div>
  );
}
