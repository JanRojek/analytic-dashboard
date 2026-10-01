import { useEffect, useRef, useState } from "react";
import { useProject, useProjects } from "../features/projects/hooks";
import {
  NavLink,
  Outlet,
  useNavigate,
  useParams,
} from "react-router-dom";
import {
  Alert,
  AppShell,
  Burger,
  Button,
  Menu,
  Modal,
  TextInput,
} from "@mantine/core";

import { Brand } from "../components/Brand.tsx";
import { ErrorState, LoadingState } from "../components/Feedback.tsx";
import { Icon } from "../components/Icon.tsx";
import { useSession } from "../session/SessionContext";

import "../features/projects/styles/project-workspace.css";
import "../layout/styles/app-shell.css";

export default function AppLayout() {
  const { mode, session, signOut } = useSession();
  const navigate = useNavigate();
  const projects = useProjects();

  const [guideOpen, setGuideOpen] = useState(false);
  const [navigationOpen, setNavigationOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [signOutError, setSignOutError] = useState("");

  const searchInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (
          (event.ctrlKey || event.metaKey) &&
          event.key.toLowerCase() === "k"
      ) {
        event.preventDefault();
        setNavigationOpen(true);

        window.requestAnimationFrame(() => {
          searchInputRef.current?.focus();
        });
      }
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const normalizedSearch = search.trim().toLowerCase();

  const visibleProjects = normalizedSearch
      ? (projects.data ?? []).filter((project) =>
          project.name.toLowerCase().includes(normalizedSearch),
      )
      : (projects.data ?? []).slice(0, 5);

  const navigation = (
      <>
        <div className="sidebar-header">
          <Brand />

          <Burger
              opened={navigationOpen}
              onClick={() => setNavigationOpen(false)}
              size="sm"
              aria-label="Close navigation"
          />
        </div>

        <button
            className="workspace-switch"
            onClick={() => setGuideOpen(true)}
        >
        <span className="workspace-avatar">
          <Icon name="grid" size={17} />
        </span>

          <span>
          <strong>Personal workspace</strong>
          <small>
            {mode === "demo"
                ? "A space to explore"
                : "Your analytics, together"}
          </small>
        </span>

          <Icon name="down" size={13} />
        </button>

        <TextInput
            ref={searchInputRef}
            className="sidebar-search"
            aria-label="Find a project"
            placeholder="Find a project"
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            leftSection={<Icon name="search" size={16} />}
            rightSection={<kbd>Ctrl K</kbd>}
            rightSectionWidth={48}
        />

        <div className="nav-label">WORKSPACE</div>

        <nav aria-label="Main navigation">
          <NavLink
              to="/projects"
              end
              className={({ isActive }) =>
                  `sidebar-link ${isActive ? "active" : ""}`
              }
          >
            <Icon name="folder" />
            <span>All projects</span>
            <span className="nav-count">{projects.data?.length ?? "—"}</span>
          </NavLink>
        </nav>

        <div className="nav-label recent-label">YOUR PROJECTS</div>

        <nav aria-label="Project shortcuts">
          {visibleProjects.map((project, index) => (
              <NavLink
                  key={project.id}
                  to={`/projects/${project.id}`}
                  className={({ isActive }) =>
                      `sidebar-link project-shortcut ${isActive ? "active" : ""}`
                  }
                  onClick={() => setSearch("")}
              >
                <span className={`project-dot project-tone-${index % 3}`} />
                <span>{project.name}</span>
              </NavLink>
          ))}

          {projects.data?.length === 0 && (
              <p className="sidebar-empty">
                Your first project will appear here.
              </p>
          )}

          {projects.data &&
              projects.data.length > 0 &&
              normalizedSearch &&
              visibleProjects.length === 0 && (
                  <p className="sidebar-empty">
                    No projects match “{search.trim()}”.
                  </p>
              )}
        </nav>

        <div className="sidebar-bottom">
          <button
              className="guide-card"
              onClick={() => setGuideOpen(true)}
          >
          <span className="guide-icon">
            <Icon name="book" size={19} />
          </span>

            <strong>A little orientation.</strong>

            <span>
            From a first dataset to
            <br />
            your next discovery.
          </span>

            <span className="guide-link">
            Meet your workspace <Icon name="arrow" size={15} />
          </span>
          </button>

          <button
              className="sidebar-link"
              onClick={() => setGuideOpen(true)}
          >
            <Icon name="help" />
            <span>Workspace guide</span>
          </button>

          <div className="sidebar-version">
            APERTURE <span>EARLY ACCESS</span>
          </div>
        </div>
      </>
  );

  return (
      <AppShell
          className="app-shell"
          header={{ height: 67 }}
          navbar={{
            width: 280,
            breakpoint: "sm",
            collapsed: {
              desktop: !navigationOpen,
              mobile: !navigationOpen,
            },
          }}
          padding={0}
      >
        <a className="skip-link" href="#main-content">
          Skip to content
        </a>

        <AppShell.Header className="global-header">
          <div className="header-leading">
            <Brand />

            <Burger
                className="navigation-toggle"
                opened={navigationOpen}
                onClick={() => setNavigationOpen((opened) => !opened)}
                size="sm"
                aria-label={
                  navigationOpen ? "Close navigation" : "Open navigation"
                }
            />
          </div>

          <div className="global-header-actions">
          <span
              className={`environment-badge ${mode === "api" ? "live" : ""}`}
          >
            <span />
            {mode === "demo"
                ? "Demo workspace"
                : "Connected workspace"}
          </span>

            <Menu position="bottom-end" shadow="md" width={240}>
              <Menu.Target>
                <button
                    className="account-button"
                    aria-label="Open account menu"
                >
                <span className="avatar">
                  {session?.user.displayName
                      .split(" ")
                      .map((part) => part[0])
                      .slice(0, 2)
                      .join("") || "A"}
                </span>

                  <Icon name="down" size={12} />
                </button>
              </Menu.Target>

              <Menu.Dropdown>
                <Menu.Label>{session?.user.displayName}</Menu.Label>
                <Menu.Label>{session?.user.email}</Menu.Label>
                <Menu.Divider />

                <Menu.Item
                    leftSection={<Icon name="logout" size={15} />}
                    onClick={() =>
                        void signOut()
                            .then(() => navigate("/login"))
                            .catch((error: unknown) =>
                                setSignOutError(
                                    error instanceof Error
                                        ? error.message
                                        : "Unable to sign out.",
                                ),
                            )
                    }
                >
                  {mode === "demo"
                      ? "Leave demo workspace"
                      : "Sign out"}
                </Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </div>
        </AppShell.Header>

        <AppShell.Navbar className="workspace-navbar">
          <div className="workspace-navigation">{navigation}</div>
        </AppShell.Navbar>

        <AppShell.Main className="app-main">
          <div className="app-body">
            {signOutError && (
                <Alert
                    color="red"
                    role="alert"
                    withCloseButton
                    onClose={() => setSignOutError("")}
                >
                  {signOutError}
                </Alert>
            )}

            <main id="main-content">
              <Outlet />
            </main>

            <footer className="workspace-footer">
            <span>
              <span className="tiny-dot" />
              {mode === "demo"
                  ? "Demo data · Changes saved in this browser"
                  : "Your connected analytical workspace"}
            </span>

              <span>Room for a new perspective.</span>
            </footer>
          </div>
        </AppShell.Main>

        <Modal
            opened={guideOpen}
            onClose={() => setGuideOpen(false)}
            title="A place for every step"
            size="lg"
        >
          <p className="muted">
            A project keeps your data, questions, and dashboards in one place.
          </p>

          <div className="guide-steps">
            {[
              {
                n: "01",
                title: "Bring your data",
                text: "Start with a CSV. Each project can hold multiple datasets.",
              },
              {
                n: "02",
                title: "Get to know it",
                text: "Inspect the rows, review missing values, and prepare a clean copy.",
              },
              {
                n: "03",
                title: "Ask a question",
                text: "Choose a dimension and a measure. Explore what the numbers say.",
              },
              {
                n: "04",
                title: "Tell the story",
                text: "Bring your charts together on a dashboard canvas.",
              },
            ].map((step) => (
                <div key={step.n}>
                  <span>{step.n}</span>

                  <div>
                    <strong>{step.title}</strong>
                    <p>{step.text}</p>
                  </div>
                </div>
            ))}
          </div>

          <Button fullWidth onClick={() => setGuideOpen(false)}>
            Let’s explore <Icon name="arrow" size={16} />
          </Button>
        </Modal>
      </AppShell>
  );
}

export function ProjectWorkspace() {
  const { projectId = "" } = useParams();
  const project = useProject(projectId);

  if (project.isPending) {
    return <LoadingState />;
  }

  if (project.error || !project.data) {
    return (
        <ErrorState
            error={project.error}
            retry={() => void project.refetch()}
        />
    );
  }

  return (
      <>
        <div className="project-workspace-head">
          <div className="project-title">
          <span className="project-monogram">
            <Icon name="folder" size={22} />
          </span>

            <div>
              <span className="eyebrow">PROJECT WORKSPACE</span>
              <div className="project-name">{project.data.name}</div>
            </div>
          </div>

          <nav className="project-tabs" aria-label="Project navigation">
            <NavLink to={`/projects/${projectId}`} end>
              <Icon name="grid" size={16} />
              Overview
            </NavLink>

            <NavLink to={`/projects/${projectId}/data`}>
              <Icon name="data" size={16} />
              Data
            </NavLink>

            <NavLink to={`/projects/${projectId}/explore`}>
              <Icon name="chart" size={16} />
              Explore
            </NavLink>

            <NavLink to={`/projects/${projectId}/dashboards`}>
              <Icon name="dashboard" size={16} />
              Dashboards
            </NavLink>
          </nav>
        </div>

        <Outlet />
      </>
  );
}
