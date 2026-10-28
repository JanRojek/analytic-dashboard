import { useEffect, useRef, useState } from "react";
import {
  ActionIcon,
  Button,
  Modal,
  Tooltip,
} from "@mantine/core";
import { useNavigate } from "react-router-dom";

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
} from "../../../data/types";
import {
  editorScope,
  persistEditorDraft,
  readEditor,
  saveEditor,
  type EditorDocument,
} from "../../../mocks/editor";
import { useSession } from "../../../session/SessionContext";
import {
  useDeleteWidget,
  useUpdateDashboardWidgets,
} from "../../dashboards/hooks";
import { chartTypes, queryCaption } from "../chartConfig";
import { orderedWidgets } from "../dashboardUtils";
import {
  createDraftWidget,
  readDraftWidgets,
  type DraftWidget,
} from "../draftWidgets";
import { DashboardCanvas } from "./DashboardCanvas";
import { ChartGallery } from "./ChartGallery";
import { DraftChartInspector } from "./DraftChartInspector";
import { WidgetChart } from "./WidgetChart";
import { WidgetResults } from "./WidgetResults";
import { DashboardWorkspaceHeader } from "./DashboardWorkspaceHeader";
import { ConfiguredChartInspector } from "./ConfiguredChartInspector";

type DashboardWorkspaceProps = {
  dashboard: Dashboard;
  sourceWidgets: Widget[];
  editing: boolean;
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


  const [initial] = useState(() => {
    const stored = readEditor(scope, dashboard.id, editing);

    if (mode === "demo") {
      return stored;
    }

    const storedDocument: EditorDocument = {
      ...stored.document,
      overrides:
          editing && stored.dirty
              ? stored.document.overrides
              : {},
    };

    const serverGeometry = Object.fromEntries(
        sourceWidgets.map((widget) => [
          widget.id,
          toGridCell(widget.layout),
        ]),
    );

    const localGeometry = storedDocument.gridLayout ?? {};

    const draftGeometry = Object.fromEntries(
        Object.entries(localGeometry).filter(([id]) =>
            id.startsWith("draft-"),
        ),
    );

    const gridLayout = !editing
        ? serverGeometry
        : stored.dirty
            ? { ...serverGeometry, ...localGeometry }
            : { ...serverGeometry, ...draftGeometry };

    return {
      ...stored,
      document: {
        ...storedDocument,
        gridLayout,
      },
    };
  });

  const [document, setDocument] = useState(initial.document);
  const [dirty, setDirty] = useState(initial.dirty);
  const [selectedId, setSelectedId] = useState("");
  const [draftWidgets, setDraftWidgets] = useState<DraftWidget[]>(() =>
      readDraftWidgets(draftKey),
  );

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [showTable, setShowTable] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [saveMessage, setSaveMessage] = useState("");
  const editRevision = useRef(0);

  const widgets = orderedWidgets(
      sourceWidgets,
      mode === "api" && !editing
          ? { ...document, overrides: {} }
          : document,
  );
  const visibleDrafts = editing ? draftWidgets : [];
  const chartCount = widgets.length + visibleDrafts.length;
  const resolvedGridGeometry = resolveGridGeometry(document.gridLayout ?? {}, [
    ...widgets.map((widget) => widget.id),
    ...visibleDrafts.map((draft) => draft.id),
  ]);

  const selectedDraft = draftWidgets.find((draft) => draft.id === selectedId);

  const selected = selectedDraft
      ? undefined
      : widgets.find((widget) => widget.id === selectedId);

  const remove = useDeleteWidget(dashboard.projectId);

  const saveWidgets = useUpdateDashboardWidgets(dashboard.projectId);

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
    editRevision.current += 1;

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

  async function persist(): Promise<void> {
    if (saveWidgets.isPending) return;

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
      return;
    }

    if (mode === "api") {
      const updates = widgets.map((widget) => ({
        widgetId: widget.id,
        datasetId: widget.datasetId,
        type: widget.type,
        title: widget.title,
        groupByColumn: widget.groupByColumn,
        measureColumn: widget.measureColumn,
        aggregation: widget.aggregation,
        layout: toWidgetLayout(
            resolvedGridGeometry[widget.id],
        ),
      }));

      const revisionAtSave = editRevision.current;

      try {
        await saveWidgets.mutateAsync({
          dashboardId: dashboard.id,
          updates,
        });

        if (editRevision.current !== revisionAtSave) {
          setSaveError("");
          return;
        }
      } catch (error) {
        setSaveError(
            error instanceof Error
                ? error.message
                : "The dashboard could not be saved.",
        );
        return;
      }
    }


    try {
      const nextDocument: EditorDocument =
          mode === "api"
              ? {
                ...document,
                overrides: {},
                gridLayout: resolvedGridGeometry,
              }
              : document;

      const saved = saveEditor(
          scope,
          dashboard.id,
          nextDocument,
      );

      setDocument(saved);
      setDirty(false);
      setSaveError("");

      setSaveMessage(
          mode === "api"
              ? "Dashboard saved to server"
              : "Saved in this browser",
      );
    } catch {
      setSaveError(
          mode === "api"
              ? "Dashboard saved to server, but local editor settings could not be saved."
              : "Your browser could not save these edits.",
      );
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
    update({ ...document, gridLayout });
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

  const saveStatus = dirty
      ? "Layout draft kept locally"
      : draftWidgets.length > 0 && editing
          ? `${draftWidgets.length} chart draft${draftWidgets.length === 1 ? "" : "s"} kept locally`
          : saveMessage ||
          (mode === "api"
              ? "Layout loaded from server"
              : "Saved in this browser");

  return (
      <div
          className={`dashboard-workspace ${editing ? "is-editing" : "is-viewing"}`}
      >
        <DashboardWorkspaceHeader
            dashboardName={dashboard.name}
            editing={editing}
            isDemo={mode === "demo"}
            dirty={dirty}
            saveStatus={saveStatus}
            onBack={() =>
                leave(`/projects/${dashboard.projectId}/dashboards`)
            }
            onPreview={() =>
                navigate(
                    `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/view`,
                )
            }
            onSave={persist}
            onEdit={() =>
                navigate(
                    `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/edit`,
                )
            }
        />

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
                  <DashboardCanvas
                      editing={editing}
                      geometry={document.gridLayout ?? {}}
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
                              fillHeight
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
                  Chart positions and sizes are kept in this browser. Use Save changes
                  to publish them to Preview.
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
                        layout={toWidgetLayout(
                          resolvedGridGeometry[selectedDraft.id],
                        )}
                        onChange={(patch) => changeDraft(selectedDraft.id, patch)}
                        onDiscard={() => discardDraft(selectedDraft.id)}
                        onCreated={(widgetId) => {
                          const nextLayout = { ...(document.gridLayout ?? {}) };
                          const draftGeometry =
                            resolvedGridGeometry[selectedDraft.id];
                          delete nextLayout[selectedDraft.id];
                          if (draftGeometry) nextLayout[widgetId] = draftGeometry;
                          update({ ...document, gridLayout: nextLayout });

                          setDraftWidgets((current) =>
                              current.filter((draft) => draft.id !== selectedDraft.id),
                          );
                          selectChart(widgetId);
                        }}
                    />
                ) : selected ? (
                    <ConfiguredChartInspector
                        key={selected.id}
                        widget={selected}
                        projectId={dashboard.projectId}
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

                    remove.mutate(
                        {
                          dashboardId: dashboard.id,
                          widgetId,
                        },
                        {
                          onSuccess: () => {
                            const gridLayout = { ...document.gridLayout };
                            const overrides = { ...document.overrides };
                            const widths = { ...document.widths };

                            delete gridLayout[widgetId];
                            delete overrides[widgetId];
                            delete widths[widgetId];

                            update({
                              ...document,
                              gridLayout,
                              overrides,
                              widths,
                              order: document.order.filter((id) => id !== widgetId),
                            });

                            setDeleteOpen(false);
                            selectChart("");
                          },
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
