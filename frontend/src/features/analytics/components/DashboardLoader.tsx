import { Link, useParams } from "react-router-dom";

import { Brand } from "../../../components/Brand";
import { ErrorState, LoadingState } from "../../../components/Feedback";
import { useDashboardEditorState } from "../../dashboards/hooks";

import { DashboardWorkspace } from "./DashboardWorkspace";

export function DashboardLoader({ editing }: { editing: boolean }) {
  const { projectId = "", dashboardId = "" } = useParams();

  return (
    <DashboardLoaderContent
      key={`${projectId}:${dashboardId}:${editing}`}
      projectId={projectId}
      dashboardId={dashboardId}
      editing={editing}
    />
  );
}

function DashboardLoaderContent({
  projectId,
  dashboardId,
  editing,
}: {
  projectId: string;
  dashboardId: string;
  editing: boolean;
}) {
  const editorState = useDashboardEditorState(projectId, dashboardId);

  // Cached data may be stale. On each Edit/Preview mount wait until a new
  // snapshot has been fetched before constructing an editor history.
  if (!editorState.isFetchedAfterMount && !editorState.isError) {
    return (
      <main className="canvas-loading">
        <Brand />
        <LoadingState label="Opening your dashboard…" />
      </main>
    );
  }

  if (editorState.isError) {
    return (
      <main className="canvas-loading">
        <Brand />
        <Link className="text-link" to={`/projects/${projectId}/dashboards`}>
          Back to dashboards
        </Link>
        <ErrorState
          error={editorState.error}
          retry={() => void editorState.refetch()}
        />
      </main>
    );
  }

  if (!editorState.data) {
    return (
      <main className="canvas-loading">
        <Brand />
        <LoadingState label="Opening your dashboard…" />
      </main>
    );
  }

  return (
    <DashboardWorkspace
      dashboard={editorState.data.dashboard}
      sourceWidgets={editorState.data.widgets}
      editing={editing}
    />
  );
}
