import { useEffect, useState } from "react";
import { queryKeys } from "../data/queryKeys";
import {
  Link,
  NavLink,
  Outlet,
  useNavigate,
  useParams,
} from "react-router-dom";
import {
  ActionIcon,
  Alert,
  Button,
  Drawer,
  Menu,
  Modal,
  TextInput,
} from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "../data/session";
import { Brand } from "./Brand";
import { Icon } from "./Icon";
import { ErrorState, LoadingState } from "./Feedback";

export default function AppLayout() {
  const { adapter, mode, session, signOut } = useSession();
  const navigate = useNavigate();
  const { projectId } = useParams();
  const projects = useQuery({
    queryKey: queryKeys.projects.all,
    queryFn: () => adapter.listProjects(),
  });
  const [searchOpen, setSearchOpen] = useState(false);
  const [guideOpen, setGuideOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [signOutError, setSignOutError] = useState("");
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key === "k") {
        event.preventDefault();
        setSearchOpen((v) => !v);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);
  const current = projects.data?.find((p) => p.id === projectId);
  function visit(path: string) {
    setMobileOpen(false);
    setSearchOpen(false);
    navigate(path);
  }
  const navigation = (
    <>
      <Brand />
      <button className="workspace-switch" onClick={() => setGuideOpen(true)}>
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
      <button className="sidebar-search" onClick={() => setSearchOpen(true)}>
        <Icon name="search" size={16} />
        <span>Find a project</span>
        <kbd>Ctrl K</kbd>
      </button>
      <div className="nav-label">WORKSPACE</div>
      <nav aria-label="Main navigation">
        <NavLink
          to="/projects"
          end
          className={({ isActive }) =>
            `sidebar-link ${isActive ? "active" : ""}`
          }
          onClick={() => setMobileOpen(false)}
        >
          <Icon name="folder" />
          <span>All projects</span>
          <span className="nav-count">{projects.data?.length ?? "—"}</span>
        </NavLink>
      </nav>
      <div className="nav-label recent-label">YOUR PROJECTS</div>
      <nav aria-label="Project shortcuts">
        {projects.data?.slice(0, 5).map((p, index) => (
          <NavLink
            key={p.id}
            to={`/projects/${p.id}`}
            className={({ isActive }) =>
              `sidebar-link project-shortcut ${isActive ? "active" : ""}`
            }
            onClick={() => setMobileOpen(false)}
          >
            <span className={`project-dot project-tone-${index % 3}`} />
            <span>{p.name}</span>
          </NavLink>
        ))}
        {projects.data?.length === 0 && (
          <p className="sidebar-empty">Your first project will appear here.</p>
        )}
      </nav>
      <div className="sidebar-bottom">
        <button className="guide-card" onClick={() => setGuideOpen(true)}>
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
        <button className="sidebar-link" onClick={() => setGuideOpen(true)}>
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
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <aside className="sidebar">{navigation}</aside>
      <Drawer
        opened={mobileOpen}
        onClose={() => setMobileOpen(false)}
        title="Your workspace"
        size={280}
      >
        <div className="mobile-navigation">{navigation}</div>
      </Drawer>
      <div className="app-body">
        <header className="global-header">
          <div className="header-breadcrumb">
            <ActionIcon
              className="mobile-menu"
              variant="subtle"
              aria-label="Open navigation"
              onClick={() => setMobileOpen(true)}
            >
              <Icon name="menu" />
            </ActionIcon>
            <span className="header-workspace">Workspace</span>
            <Icon name="chevron" size={13} />
            <Link to="/projects">Projects</Link>
            {current && (
              <>
                <Icon name="chevron" size={13} />
                <span className="breadcrumb-current">{current.name}</span>
              </>
            )}
          </div>
          <div className="global-header-actions">
            <span
              className={`environment-badge ${mode === "api" ? "live" : ""}`}
            >
              <span />
              {mode === "demo" ? "Demo workspace" : "Connected workspace"}
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
                      .map((s) => s[0])
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
                      .catch((e: unknown) =>
                        setSignOutError(
                          e instanceof Error
                            ? e.message
                            : "Unable to sign out.",
                        ),
                      )
                  }
                >
                  {mode === "demo" ? "Leave demo workspace" : "Sign out"}
                </Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </div>
        </header>
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
      <Modal
        opened={searchOpen}
        onClose={() => setSearchOpen(false)}
        title="Find a project"
        size="md"
      >
        <TextInput
          autoFocus
          aria-label="Search all projects"
          placeholder="Search your projects…"
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          leftSection={<Icon name="search" />}
        />
        <div className="search-results">
          {projects.data
            ?.filter((p) => p.name.toLowerCase().includes(search.toLowerCase()))
            .map((p) => (
              <button key={p.id} onClick={() => visit(`/projects/${p.id}`)}>
                <Icon name="folder" />
                <span>{p.name}</span>
                <Icon name="arrow" size={16} />
              </button>
            ))}
          {projects.data?.filter((p) =>
            p.name.toLowerCase().includes(search.toLowerCase()),
          ).length === 0 && (
            <p className="muted">No projects match “{search}”.</p>
          )}
        </div>
      </Modal>
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
    </div>
  );
}

export function ProjectWorkspace() {
  const { projectId = "" } = useParams();
  const { adapter } = useSession();
  const project = useQuery({
    queryKey: queryKeys.projects.detail(projectId),
    queryFn: () => adapter.getProject(projectId),
  });
  if (project.isPending) return <LoadingState />;
  if (project.error || !project.data)
    return (
      <ErrorState error={project.error} retry={() => void project.refetch()} />
    );
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
