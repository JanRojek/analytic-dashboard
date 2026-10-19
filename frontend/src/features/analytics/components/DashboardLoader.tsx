import { Link, useParams } from "react-router-dom";

import { Brand } from "../../../components/Brand";
import { ErrorState, LoadingState } from "../../../components/Feedback";

import { useDashboard, useDashboardWidgets } from "../../dashboards/hooks";
import { DashboardWorkspace } from "./DashboardWorkspace";

export function DashboardLoader({ editing }: { editing: boolean }) {
  const { projectId = "", dashboardId = "" } = useParams();
  const dashboard = useDashboard(projectId, dashboardId);
  const widgets = useDashboardWidgets(projectId, dashboardId);
  if (dashboard.isPending || widgets.isPending)
    return (
      <main className="canvas-loading">
        <Brand />
        <LoadingState label="Opening your dashboard…" />
      </main>
    );
  if (dashboard.isError || widgets.isError)
    return (
      <main className="canvas-loading">
        <Brand />
        <Link className="text-link" to={`/projects/${projectId}/dashboards`}>
          Back to dashboards
        </Link>
        <ErrorState
          error={dashboard.error || widgets.error}
          retry={() => {
            void dashboard.refetch();
            void widgets.refetch();
          }}
        />
      </main>
    );
  return (
    <DashboardWorkspace
      key={`${dashboardId}:${editing}`}
      dashboard={dashboard.data}
      sourceWidgets={widgets.data}
      editing={editing}
    />
  );
}
