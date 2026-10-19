import { Icon } from "../../../components/Icon";
import { useDashboardWidgets } from "../../dashboards/hooks";
import { useSession } from "../../../session/SessionContext";
import { editorScope, readEditor } from "../../../mocks/editor";

import { orderedWidgets } from "../dashboardUtils";
import { WidgetChart } from "./WidgetChart";

export function DashboardThumbnail({
  projectId,
  dashboardId,
}: {
  projectId: string;
  dashboardId: string;
}) {
  const { mode, session } = useSession();
  const widgets = useDashboardWidgets(projectId, dashboardId);

  const saved = readEditor(
    editorScope(mode, session?.user.id || "", projectId),
    dashboardId,
    false,
  ).document;

  const ordered = orderedWidgets(widgets.data || [], saved);

  return (
    <div className="dashboard-thumbnail" aria-hidden="true">
      <div className="thumbnail-heading">
        <b />
        <span />
      </div>

      {ordered.length ? (
        <div className="thumbnail-charts">
          {ordered.slice(0, 2).map((widget) => (
            <div key={widget.id}>
              <span className="thumbnail-chart-title">{widget.title}</span>
              <WidgetChart widget={widget} projectId={projectId} compact />
            </div>
          ))}
        </div>
      ) : (
        <div className="thumbnail-empty">
          <Icon name="dashboard" size={30} />
          <span>
            {widgets.isPending
              ? "Loading canvas…"
              : "Your next story starts here"}
          </span>
        </div>
      )}
    </div>
  );
}
