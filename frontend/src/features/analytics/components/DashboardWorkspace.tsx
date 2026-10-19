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

import type { Dashboard, Widget, WidgetInput } from "../../../data/types";

import {
  editorScope,
  persistEditorDraft,
  readEditor,
  saveEditor,
  type EditorDocument,
} from "../../../mocks/editor";

import { useSession } from "../../../session/SessionContext";
import { useDatasets } from "../../data/hooks";
import { useDeleteWidget } from "../../dashboards/hooks";

import { chartTypes, queryCaption } from "../chartConfig";
import { orderedWidgets } from "../dashboardUtils";

import { ChartTypePicker } from "./ChartTypePicker";
import { InspectorQuery } from "./InspectorQuery";
import { NewChartDialog } from "./NewChartDialog";
import { WidgetChart } from "./WidgetChart";
import { WidgetResults } from "./WidgetResults";

export function DashboardWorkspace({
  dashboard,
  sourceWidgets,
  editing,
}: {
  dashboard: Dashboard;
  sourceWidgets: Widget[];
  editing: boolean;
}) {
  const { mode, session } = useSession();
  const navigate = useNavigate();
  const scope = editorScope(mode, session?.user.id || "", dashboard.projectId);
  const [initial] = useState(() => readEditor(scope, dashboard.id, editing));
  const [document, setDocument] = useState(initial.document);
  const [dirty, setDirty] = useState(initial.dirty);
  const [selectedId, setSelectedId] = useState(sourceWidgets[0]?.id || "");
  const [addOpen, setAddOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [saveError, setSaveError] = useState("");
  const [saveMessage, setSaveMessage] = useState("");
  const [showTable, setShowTable] = useState(false);
  const widgets = orderedWidgets(sourceWidgets, document);
  const selected =
    widgets.find((widget) => widget.id === selectedId) || widgets[0];
  const datasets = useDatasets(dashboard.projectId, editing);
  useEffect(() => {
    if (!dirty || !editing) return;
    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, [dirty, editing]);
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
  function persist() {
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
    )
      return;
    navigate(path);
  }
  function changeWidget(patch: Partial<WidgetInput>) {
    if (selected)
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
    if (index + direction < 0 || index + direction >= ids.length) return;
    [ids[index], ids[index + direction]] = [ids[index + direction], ids[index]];
    update({ ...document, order: ids });
  }
  const remove = useDeleteWidget(dashboard.projectId);
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
            )
              event.preventDefault();
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
              <Tooltip label="View your saved dashboard">
                <Button
                  variant="default"
                  size="sm"
                  leftSection={<Icon name="eye" size={16} />}
                  onClick={() => {
                    if (!dirty || persist())
                      navigate(
                        `/projects/${dashboard.projectId}/dashboards/${dashboard.id}/view`,
                      );
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
      {editing && (
        <div className="canvas-toolbar">
          <div>
            <span
              className={dirty ? "save-indicator unsaved" : "save-indicator"}
            />
            <span role="status">
              {dirty
                ? "Unsaved changes · draft kept locally"
                : saveMessage || "Layout and edits saved in this browser"}
            </span>
          </div>
          <Button
            size="xs"
            variant="subtle"
            leftSection={<Icon name="plus" size={15} />}
            onClick={() => setAddOpen(true)}
          >
            Add chart
          </Button>
        </div>
      )}
      {saveError && (
        <div className="analytics-storage-error" role="alert">
          {saveError}
        </div>
      )}
      <div className="canvas-work-area">
        {editing && (
          <aside className="canvas-outline">
            <div className="canvas-panel-heading">
              Outline <span>{widgets.length}</span>
            </div>
            <p className="canvas-panel-hint">Your dashboard at a glance</p>
            <div className="outline-items">
              {widgets.map((widget, index) => (
                <button
                  key={widget.id}
                  className={selected?.id === widget.id ? "selected" : ""}
                  onClick={() => {
                    setSelectedId(widget.id);
                    setShowTable(false);
                  }}
                  aria-pressed={selected?.id === widget.id}
                >
                  <Icon
                    name={
                      chartTypes.find((type) => type.value === widget.type)
                        ?.icon || "chart"
                    }
                    size={16}
                  />
                  <span>{widget.title}</span>
                  <small>{index + 1}</small>
                </button>
              ))}
            </div>
            <button className="outline-add" onClick={() => setAddOpen(true)}>
              <Icon name="plus" size={16} />
              Add a chart
            </button>
            <div className="canvas-outline-bottom">
              <Icon name="help" size={17} />
              <p>Select a chart to adjust its question, style and position.</p>
            </div>
          </aside>
        )}
        <main className="canvas-stage">
          <div className="canvas-size-label">
            {editing ? "DASHBOARD CANVAS" : "PROJECT DASHBOARD"}
            <span>
              {widgets.length} {widgets.length === 1 ? "chart" : "charts"}
            </span>
          </div>
          <div className="dashboard-paper">
            <div className="dashboard-paper-heading">
              <span className="eyebrow">AN OVERVIEW THAT MATTERS</span>
              <h1>{dashboard.name}</h1>
              <p>
                {editing
                  ? "Bring your findings into focus."
                  : "A clear view of your latest data."}
              </p>
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
            {widgets.length ? (
              <div className="canvas-chart-grid">
                {widgets.map((widget) => (
                  <section
                    key={widget.id}
                    className={`canvas-chart ${document.widths[widget.id] === "full" ? "full-width" : ""} ${editing && selected?.id === widget.id ? "is-selected" : ""} ${widget.type === "Kpi" ? "is-kpi" : ""}`}
                    onClick={(event) => {
                      if (
                        editing &&
                        !(event.target as Element).closest(
                          "button, a, input, select",
                        )
                      ) {
                        setSelectedId(widget.id);
                        setShowTable(false);
                      }
                    }}
                  >
                    <div className="canvas-chart-heading">
                      <div>
                        <h2>{widget.title}</h2>
                        <p>{queryCaption(widget)}</p>
                      </div>
                      {editing && (
                        <Tooltip label={`Configure ${widget.title}`}>
                          <ActionIcon
                            variant={
                              selected?.id === widget.id ? "light" : "subtle"
                            }
                            aria-label={`Configure ${widget.title}`}
                            onClick={() => {
                              setSelectedId(widget.id);
                              setShowTable(false);
                            }}
                          >
                            <Icon name="settings" size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                      {!editing && (
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
                    />
                  </section>
                ))}
              </div>
            ) : (
              <EmptyState
                icon="chart"
                title="What would you like to understand?"
                description="Add a chart from your project data. Build a story by bringing your findings together."
                action={
                  editing && (
                    <Button
                      leftSection={<Icon name="plus" />}
                      onClick={() => setAddOpen(true)}
                    >
                      Add your first chart
                    </Button>
                  )
                }
              />
            )}
            <footer className="dashboard-paper-footer">
              <span>Aperture</span>
              <span>
                {editing
                  ? "Designed for a clearer perspective"
                  : "Your data. A clearer perspective."}
              </span>
            </footer>
          </div>
          {editing && (
            <p className="canvas-stage-hint">
              <Icon name="shield" size={14} />
              New charts are saved to your{" "}
              {mode === "demo" ? "demo workspace" : "account"}. Layout and chart
              edits stay in this browser.
            </p>
          )}
        </main>
        {editing && (
          <aside className="canvas-inspector">
            <div className="canvas-panel-heading">
              Chart properties <Icon name="settings" size={16} />
            </div>
            {selected ? (
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
                  <span className="inspector-label">Visualization</span>
                  <ChartTypePicker
                    value={selected.type}
                    onChange={(type) => changeWidget({ type })}
                  />
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
                  <div className="inspector-reorder">
                    <span>Position</span>
                    <Button
                      variant="default"
                      size="compact-xs"
                      disabled={widgets[0].id === selected.id}
                      onClick={() => move(-1)}
                    >
                      Move earlier
                    </Button>
                    <Button
                      variant="default"
                      size="compact-xs"
                      disabled={widgets[widgets.length - 1].id === selected.id}
                      onClick={() => move(1)}
                    >
                      Move later
                    </Button>
                  </div>
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
                <p>Add a chart, then make it your own here.</p>
              </div>
            )}
          </aside>
        )}
      </div>
      <NewChartDialog
        opened={addOpen}
        onClose={() => setAddOpen(false)}
        dashboard={dashboard}
        datasets={datasets.data || []}
        datasetsLoading={datasets.isPending}
        datasetsError={datasets.error}
        retryDatasets={() => datasets.refetch()}
        onCreated={(id) => {
          setSelectedId(id);
          setAddOpen(false);
        }}
      />
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
