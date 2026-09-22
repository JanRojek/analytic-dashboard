import {
  Component,
  lazy,
  Suspense,
  useLayoutEffect,
  type ErrorInfo,
  type ReactNode,
} from "react";
import { Navigate, Outlet, Route, Routes, useLocation } from "react-router-dom";
import AppLayout, { ProjectWorkspace } from "./components/AppLayout";
import { useSession } from "./data/session";
import { EmptyState, ErrorState, LoadingState } from "./components/Feedback";
import { Button } from "@mantine/core";

const ProjectsPage = lazy(() => import("./pages/ProjectsPage"));
const ProjectOverviewPage = lazy(async () => ({
  default: (await import("./pages/ProjectsPage")).ProjectOverviewPage,
}));
const AuthPage = lazy(() => import("./pages/AuthPage"));
const DataPage = lazy(async () => ({
  default: (await import("./pages/DataPages")).DataPage,
}));
const DatasetPage = lazy(async () => ({
  default: (await import("./pages/DataPages")).DatasetPage,
}));
const ExplorePage = lazy(async () => ({
  default: (await import("./pages/AnalyticsPages")).ExplorePage,
}));
const DashboardsPage = lazy(async () => ({
  default: (await import("./pages/AnalyticsPages")).DashboardsPage,
}));
const DashboardEditorPage = lazy(async () => ({
  default: (await import("./pages/AnalyticsPages")).DashboardEditorPage,
}));
const DashboardViewPage = lazy(async () => ({
  default: (await import("./pages/AnalyticsPages")).DashboardViewPage,
}));

function ProtectedWorkspace() {
  const { session, loading, restorationError, retrySession, clearSession } =
    useSession();
  if (loading) return <LoadingState label="Restoring your workspace…" />;
  if (restorationError)
    return (
      <div className="page-content">
        <ErrorState error={restorationError} retry={retrySession} />
        <p className="muted">
          Your saved sign-in has been kept. Retry when the server is available,
          or return to sign in to open the demo.
        </p>
        <Button variant="default" mt="lg" onClick={clearSession}>
          Back to sign in
        </Button>
      </div>
    );
  return session ? <Outlet /> : <Navigate to="/login" replace />;
}
class ApplicationBoundary extends Component<
  { children: ReactNode },
  { error: boolean }
> {
  state = { error: false };
  static getDerivedStateFromError() {
    return { error: true };
  }
  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(
      "Workspace rendering failed",
      error.message,
      info.componentStack,
    );
  }
  render() {
    return this.state.error ? (
      <EmptyState
        icon="warning"
        title="Let’s get you back on track."
        description="This view could not be displayed. Your saved work is still in place."
        action={
          <Button onClick={() => window.location.assign("/projects")}>
            Return to projects
          </Button>
        }
      />
    ) : (
      this.props.children
    );
  }
}
function AuthRoute() {
  const location = useLocation();
  return <AuthPage key={location.pathname} />;
}
function RoutePosition() {
  const { pathname } = useLocation();
  useLayoutEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: "instant" });
  }, [pathname]);
  return null;
}
export default function App() {
  return (
    <ApplicationBoundary>
      <RoutePosition />
      <Suspense fallback={<LoadingState label="Opening your workspace…" />}>
        <Routes>
          <Route path="/" element={<Navigate to="/projects" replace />} />
          {[
            "login",
            "register",
            "forgot-password",
            "reset-password",
            "confirm-email",
          ].map((path) => (
            <Route key={path} path={path} element={<AuthRoute />} />
          ))}
          <Route element={<ProtectedWorkspace />}>
            <Route element={<AppLayout />}>
              <Route path="projects" element={<ProjectsPage />} />
              <Route path="projects/:projectId" element={<ProjectWorkspace />}>
                <Route index element={<ProjectOverviewPage />} />
                <Route path="data" element={<DataPage />} />
                <Route path="data/:datasetId" element={<DatasetPage />} />
                <Route path="explore" element={<ExplorePage />} />
                <Route path="dashboards" element={<DashboardsPage />} />
              </Route>
            </Route>
            <Route
              path="projects/:projectId/dashboards/:dashboardId/edit"
              element={<DashboardEditorPage />}
            />
            <Route
              path="projects/:projectId/dashboards/:dashboardId/view"
              element={<DashboardViewPage />}
            />
          </Route>
          <Route
            path="*"
            element={
              <EmptyState
                title="This page wandered off."
                description="Head back to your projects to find your bearings."
                action={
                  <Button component="a" href="/projects">
                    Back to projects
                  </Button>
                }
              />
            }
          />
        </Routes>
      </Suspense>
    </ApplicationBoundary>
  );
}
