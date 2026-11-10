import { useCallback, useEffect, useState } from "react";
import { Button, Modal } from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { addWidget, removeWidget, updateWidget, updateWidgetLayouts } from "../editor/editorState";
import { createEditorSnapshot } from "../editor/editorSnapshot";
import { useEditorHistory } from "../editor/useEditorHistory";
import { useDashboardAutosave, type DashboardAutosaveStatus } from "../editor/useDashboardAutosave";
import { readEditorSnapshotDraft } from "../editor/editorSnapshotDraft";

import { EmptyState, ErrorState } from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import {
  resolveGridGeometry,
  toGridCell,
  toWidgetLayout,
} from "../../../data/dashboardGrid";
import type {
  Dashboard,
  Widget,
  WidgetInput,
  WidgetType,
  WidgetLayout,
} from "../../../data/types";
import {
  editorScope,
  persistEditorDraft,
  readEditor,
  saveEditor,
  type EditorDocument,
} from "../../../mocks/editor";
import { useSession } from "../../../session/SessionContext";
import { useDatasetProfile } from "../../data/hooks";
import { useDeleteWidget } from "../../dashboards/hooks";
import { chartTypes } from "../chartConfig";
import { orderedWidgets } from "../dashboardUtils";
import {
  createDraftWidget,
  readDraftWidgets,
  type DraftWidget,
} from "../draftWidgets";
import { DashboardCanvas } from "./DashboardCanvas";
import { ChartGallery } from "./ChartGallery";
import { DraftChartInspector } from "./DraftChartInspector";
import { DashboardWidgetContent } from "./DashboardWidgetContent";
import { WidgetResults } from "./WidgetResults";
import { DashboardWorkspaceHeader } from "./DashboardWorkspaceHeader";
import { ConfiguredChartInspector } from "./ConfiguredChartInspector";

type DashboardWorkspaceProps = {
  dashboard: Dashboard;
  sourceWidgets: Widget[];
  editing: boolean;
};

const AUTOSAVE_MESSAGES: Record<DashboardAutosaveStatus, string> = {
  saved: "All changes saved",
  pending: "Waiting to save…",
  saving: "Saving changes…",
  invalid: "Complete chart configuration to save",
  error: "Autosave failed — changes kept locally",
  conflict: "Save conflict — changes kept locally",
};

export function DashboardWorkspace({
  dashboard,
  sourceWidgets,
  editing,
}: DashboardWorkspaceProps) {
  const { mode, session } = useSession();
  const navigate = useNavigate();
  const scope = editorScope(mode, session?.user.id || "", dashboard.projectId);
  const draftKey = `aperture:widget-drafts:${scope}:${dashboard.id}`;
  const snapshotDraftKey = `${scope}:editor:${dashboard.id}:snapshot-draft`;

  const [initial] = useState(() => {
    const stored = readEditor(scope, dashboard.id, editing);

    if (mode === "demo") {
      return stored;
    }

    const draftGeometry = Object.fromEntries(
        Object.entries(stored.document.gridLayout ?? {}).filter(([id]) =>
            id.startsWith("draft-"),
        ),
    );

    return {
      ...stored,
      document: {
        ...stored.document,
        order: [],
        widths: {},
        overrides: {},
        gridLayout: draftGeometry,
      },
    };
  });

  const [document, setDocument] = useState(initial.document);
  const [recoveredDraft] = useState(() =>
      mode === "api" && editing
          ? readEditorSnapshotDraft(snapshotDraftKey)
          : null,
  );
  const [initialSnapshot] = useState(() =>
      recoveredDraft?.snapshot ??
      (mode === "api"
          ? {
            widgets: sourceWidgets.map((widget) => ({
              ...widget,
              layout: { ...widget.layout },
            })),
          }
          : createEditorSnapshot(sourceWidgets, initial.document)),
  );

  const editor = useEditorHistory(initialSnapshot);
  const [dirty, setDirty] = useState(mode === "demo" && initial.dirty);
  const [selectedId, setSelectedId] = useState("");
  const [editingInspectorField, setEditingInspectorField] = useState(false);
  const [draftWidgets, setDraftWidgets] = useState<DraftWidget[]>(() =>
      readDraftWidgets(draftKey),
  );

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [showTable, setShowTable] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [saveMessage, setSaveMessage] = useState("");

  const autosave = useDashboardAutosave({
    enabled: mode === "api" && editing,
    suspend: editingInspectorField,
    projectId: dashboard.projectId,
    dashboardId: dashboard.id,
    draftKey: snapshotDraftKey,
    snapshot: editor.snapshot,
    baseRevision: recoveredDraft?.baseRevision ?? dashboard.revision,
    serverRevision: dashboard.revision,
    hasRecoveredDraft: Boolean(recoveredDraft),
  });

  const widgets =
      mode === "api"
          ? editing
              ? editor.snapshot.widgets
              : sourceWidgets
          : orderedWidgets(sourceWidgets, document);
  const visibleDrafts = editing ? draftWidgets : [];
  const chartCount = widgets.length + visibleDrafts.length;
  const canvasGeometry =
      mode === "api"
          ? {
            ...Object.fromEntries(
                widgets.map((widget) => [widget.id, toGridCell(widget.layout)]),
            ),
            ...(editing ? document.gridLayout : {}),
          }
          : (document.gridLayout ?? {});

  const resolvedGridGeometry = resolveGridGeometry(canvasGeometry, [
    ...widgets.map((widget) => widget.id),
    ...visibleDrafts.map((draft) => draft.id),
  ]);

  const selectedDraft = draftWidgets.find((draft) => draft.id === selectedId);

  const selected = selectedDraft
      ? undefined
      : widgets.find((widget) => widget.id === selectedId);

  const selectedDatasetProfile = useDatasetProfile(
      dashboard.projectId,
      selected?.datasetId ?? "",
      editing && Boolean(selected),
  );

  const remove = useDeleteWidget(dashboard.projectId);
  const hasUnsavedChanges =
      mode === "api" ? autosave.status !== "saved" : dirty;

  useEffect(() => {
    if (!hasUnsavedChanges || !editing) return;

    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };

    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, [hasUnsavedChanges, editing]);

  useEffect(() => {
    if (!editing) return;

    try {
      localStorage.setItem(draftKey, JSON.stringify(draftWidgets));
    } catch {
      setSaveError("Your browser could not save chart drafts.");
    }
  }, [draftKey, draftWidgets, editing]);

  useEffect(() => {
    if (mode !== "demo" || !editing || !dirty) return;

    const timeout = window.setTimeout(() => {
      try {
        const saved = saveEditor(scope, dashboard.id, document);
        setDocument(saved);
        setDirty(false);
        setSaveMessage("Saved in this browser");
        setSaveError("");
      } catch {
        setSaveError("Your browser could not save these edits.");
      }
    }, 800);

    return () => window.clearTimeout(timeout);
  }, [mode, editing, dirty, scope, dashboard.id, document]);

  const runHistoryAction = useCallback(
      (action: "undo" | "redo") => {
        if (mode !== "api" || !editing) return;

        if (action === "undo") {
          if (!editor.canUndo) return;
          editor.undo();
        } else {
          if (!editor.canRedo) return;
          editor.redo();
        }
      },
      [
        mode,
        editing,
        editor.canUndo,
        editor.canRedo,
        editor.undo,
        editor.redo,
      ],
  );

  useEffect(() => {
    if (mode !== "api" || !editing) return;

    const handleHistoryShortcut = (event: KeyboardEvent) => {
      if (
          event.defaultPrevented ||
          event.isComposing ||
          !(event.ctrlKey || event.metaKey) ||
          event.altKey
      ) {
        return;
      }

      if (
          event.target instanceof Element &&
          event.target.closest(
              'input, textarea, select, [contenteditable], [role="textbox"]',
          )
      ) {
        return;
      }

      const key = event.key.toLowerCase();
      if (key !== "z" && key !== "y") return;

      const action =
          key === "y" || (key === "z" && event.shiftKey) ? "redo" : "undo";

      if (
          (action === "undo" && !editor.canUndo) ||
          (action === "redo" && !editor.canRedo)
      ) {
        return;
      }

      event.preventDefault();
      runHistoryAction(action);
    };

    window.addEventListener("keydown", handleHistoryShortcut);
    return () => window.removeEventListener("keydown", handleHistoryShortcut);
  }, [
    mode,
    editing,
    editor.canUndo,
    editor.canRedo,
    runHistoryAction,
  ]);

  function update(next: EditorDocument) {
    setDocument(next);
    if (mode === "demo") {
      setDirty(true);
      setSaveMessage("");
    }

    try {
      persistEditorDraft(scope, dashboard.id, next);
      setSaveError("");
    } catch {
      setSaveError(
          "Your browser could not save this draft. Free some browser storage before leaving.",
      );
    }
  }

  function leave(path: string) {
    if (
        hasUnsavedChanges &&
        !window.confirm(
            "Some changes are not saved yet. Your local draft will remain in this browser, but Preview may show the last saved version. Leave the editor?",
        )
    ) {
      return;
    }

    navigate(path);
  }

  const selectChart = useCallback((id: string) => {
    setEditingInspectorField(false);
    setSelectedId(id);
    setShowTable(false);
  }, []);

  const inspectChartResults = useCallback((id: string) => {
    setSelectedId(id);
    setShowTable(true);
  }, []);

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
    if (document.gridLayout?.[id]) {
      const nextLayout = { ...document.gridLayout };
      delete nextLayout[id];
      update({ ...document, gridLayout: nextLayout });
    }
    setDraftWidgets((current) => current.filter((draft) => draft.id !== id));
    selectChart("");
  }

  function commitGridGeometry(
      gridLayout: NonNullable<EditorDocument["gridLayout"]>,
  ) {
    if (mode === "api") {
      editor.apply((snapshot) => {
        const layouts: Record<string, WidgetLayout> = {};

        for (const widget of snapshot.widgets) {
          const geometry = gridLayout[widget.id];
          if (geometry) layouts[widget.id] = toWidgetLayout(geometry);
        }

        return updateWidgetLayouts(snapshot, layouts);
      });

      const draftGeometry = Object.fromEntries(
          Object.entries(gridLayout).filter(([id]) => id.startsWith("draft-")),
      );
      update({ ...document, gridLayout: draftGeometry });
      return;
    }

    update({ ...document, gridLayout });
  }

  function changeWidget(patch: Partial<WidgetInput>, groupId?: string) {
    if (!selected) return;

    if (mode === "api") {
      editor.apply(
          (snapshot) => updateWidget(snapshot, selected.id, patch),
          groupId,
      );
      return;
    }

    update({
      ...document,
      overrides: {
        ...document.overrides,
        [selected.id]: {
          ...document.overrides[selected.id],
          ...patch,
        },
      },
    });
  }

  const draftStatus = `${draftWidgets.length} chart draft${draftWidgets.length === 1 ? "" : "s"} kept locally`;
  const saveStatus =
      mode === "api"
          ? editingInspectorField && autosave.status === "pending"
              ? "Finish editing to save…"
              : autosave.status === "saved" && draftWidgets.length > 0 && editing
                  ? draftStatus
                  : AUTOSAVE_MESSAGES[autosave.status]
          : dirty
              ? "Saving locally…"
              : draftWidgets.length > 0 && editing
                  ? draftStatus
                  : saveMessage || "Saved in this browser";

  const datasetError =
      editing && selected && selectedDatasetProfile.isError
          ? selectedDatasetProfile.error instanceof Error
              ? selectedDatasetProfile.error.message
              : "Could not load the selected dataset."
          : null;
  const autosaveError = mode === "api" ? autosave.error : null;
  const bannerError = saveError || autosaveError || datasetError;
  const retryAutosave = mode === "api" && autosave.status === "error";
  const retryDataset = Boolean(datasetError);
  const showBannerRetry =
      !saveError && (autosaveError ? retryAutosave : retryDataset);

  function retryBanner() {
    if (retryAutosave) autosave.retry();
    if (retryDataset) void selectedDatasetProfile.refetch();
  }

  return (
      <div
          className={`dashboard-workspace ${editing ? "is-editing" : "is-viewing"}`}
      >
        <DashboardWorkspaceHeader
            dashboardName={dashboard.name}
            editing={editing}
            isDemo={mode === "demo"}
            dirty={hasUnsavedChanges}
            saveStatus={saveStatus}
            onBack={() => leave(`/projects/${dashboard.projectId}/dashboards`)}
            onPreview={() =>
                leave(`/projects/${dashboard.projectId}/dashboards/${dashboard.id}/view`)
            }
            onEdit={() =>
                navigate(
                    `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/edit`,
                )
            }
        />

        {bannerError && (
            <div
                className="analytics-storage-error"
                role="alert"
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 16,
                }}
            >
              <span>{bannerError}</span>
              {showBannerRetry && (
                  <Button
                      type="button"
                      variant="subtle"
                      color="orange"
                      size="xs"
                      onClick={retryBanner}
                      style={{ flexShrink: 0 }}
                  >
                    Try again
                  </Button>
              )}
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
                  <DashboardCanvas
                      editing={editing}
                      geometry={canvasGeometry}
                      onGeometryCommit={editing ? commitGridGeometry : undefined}
                  >
                    {widgets.map((widget) => (
                        <section
                            key={widget.id}
                            data-layout-id={widget.id}
                            className={[
                              "canvas-chart",
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
                          <DashboardWidgetContent
                              widget={widget}
                              projectId={dashboard.projectId}
                              editing={editing}
                              selected={selected?.id === widget.id}
                              onSelect={selectChart}
                              onInspectResults={inspectChartResults}
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
                              data-layout-id={draft.id}
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
                  {mode === "api"
                      ? "Changes save automatically after a short pause. Preview shows the last server-saved version."
                      : "Layout changes are saved automatically in this browser."}
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
                        layout={toWidgetLayout(resolvedGridGeometry[selectedDraft.id])}
                        onChange={(patch) => changeDraft(selectedDraft.id, patch)}
                        onDiscard={() => discardDraft(selectedDraft.id)}
                        onCreated={(widget) => {
                          if (mode === "api") {
                            editor.apply((snapshot) => addWidget(snapshot, widget));
                          }

                          const nextLayout = { ...(document.gridLayout ?? {}) };
                          const draftGeometry = resolvedGridGeometry[selectedDraft.id];
                          delete nextLayout[selectedDraft.id];
                          if (mode === "demo" && draftGeometry) {
                            nextLayout[widget.id] = draftGeometry;
                          }
                          update({ ...document, gridLayout: nextLayout });

                          setDraftWidgets((current) =>
                              current.filter((item) => item.id !== selectedDraft.id),
                          );
                          selectChart(widget.id);
                        }}
                    />
                ) : selected ? (
                    <div
                        onFocusCapture={(event) => {
                          const target = event.target;
                          if (
                              target instanceof HTMLElement &&
                              target.matches('input, textarea, [contenteditable="true"]')
                          ) {
                            setEditingInspectorField(true);
                          }
                        }}
                        onBlurCapture={(event) => {
                          const target = event.target;
                          if (
                              !(target instanceof HTMLElement) ||
                              !target.matches('input, textarea, [contenteditable="true"]')
                          ) {
                            return;
                          }

                          const next = event.relatedTarget;
                          if (
                              next instanceof Element &&
                              event.currentTarget.contains(next) &&
                              next.matches('input, textarea, [contenteditable="true"]')
                          ) {
                            return;
                          }

                          setEditingInspectorField(false);
                        }}
                    >
                      <ConfiguredChartInspector
                          key={selected.id}
                          widget={selected}
                          projectId={dashboard.projectId}
                          saveCompleted={mode === "api" && autosave.status === "saved" && !editingInspectorField}
                          onChange={changeWidget}
                          onOpenDataset={() =>
                              leave(
                                  `/projects/${dashboard.projectId}/data/${selected.datasetId}`,
                              )
                          }
                          onInspectResults={() => setShowTable(true)}
                          onRemove={() => {
                            remove.reset();
                            setDeleteOpen(true);
                          }}
                      />
                    </div>
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

                    const widgetId = selected.id;

                    const gridLayout = { ...document.gridLayout };
                    const overrides = { ...document.overrides };
                    const widths = { ...document.widths };

                    delete gridLayout[widgetId];
                    delete overrides[widgetId];
                    delete widths[widgetId];

                    const nextDocument: EditorDocument = {
                      ...document,
                      gridLayout,
                      overrides,
                      widths,
                      order: document.order.filter((id) => id !== widgetId),
                    };

                    const finishRemove = () => {
                      update(nextDocument);
                      setDeleteOpen(false);
                      selectChart("");
                    };

                    if (mode === "api") {
                      editor.apply((snapshot) => removeWidget(snapshot, widgetId));
                      setDeleteOpen(false);
                      selectChart("");
                    } else {
                      remove.mutate(
                          { dashboardId: dashboard.id, widgetId },
                          { onSuccess: finishRemove },
                      );
                    }
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
