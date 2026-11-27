import test, { after, before } from "node:test";
import assert from "node:assert/strict";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { createServer } from "vite";
import react from "@vitejs/plugin-react";
import { queryKeys } from "../src/data/queryKeys.ts";
import { demoAdapter } from "../src/mocks/adapter.ts";
import { createSeed } from "../src/mocks/seed.ts";
import { DEMO_STORAGE_KEY } from "../src/mocks/store.ts";

let vite;
let DataPage;
let DatasetSelect;
let ProjectOverviewPage;
let SessionContext;
let useImportDataset;
let useDeleteDataset;
let useCreateProject;
let useRenameProject;
let useDeleteProject;
let useCreateDashboard;
let useDeleteDashboard;
let ProjectsPage;
let DashboardsPage;
let AppLayout;

before(async () => {
  vite = await createServer({
    configFile: false,
    plugins: [react()],
    optimizeDeps: { noDiscovery: true, include: [] },
    server: { middlewareMode: true, watch: null, hmr: false },
    appType: "custom",
  });
  ({ DataPage } = await vite.ssrLoadModule(
      "/src/features/data/pages/DataPage.tsx",
  ));
  ({ DatasetSelect } = await vite.ssrLoadModule(
      "/src/features/data/components/DatasetSelect.tsx",
  ));
  ({ default: ProjectOverviewPage } = await vite.ssrLoadModule(
      "/src/features/projects/pages/ProjectOverviewPage.tsx",
  ));
  ({ SessionContext } = await vite.ssrLoadModule(
      "/src/session/SessionContext.ts",
  ));
  ({ useCreateProject, useRenameProject, useDeleteProject } =
      await vite.ssrLoadModule("/src/features/projects/hooks.ts"));
  ({ useCreateDashboard, useDeleteDashboard } = await vite.ssrLoadModule(
      "/src/features/dashboards/hooks.ts",
  ));
  ({ default: ProjectsPage } = await vite.ssrLoadModule(
      "/src/features/projects/pages/ProjectsPage.tsx",
  ));
  ({ DashboardsPage } = await vite.ssrLoadModule(
      "/src/features/analytics/pages/DashboardsPage.tsx",
  ));
  ({ default: AppLayout } = await vite.ssrLoadModule(
      "/src/layout/AppLayout.tsx",
  ));
  ({ useImportDataset, useDeleteDataset } =
      await vite.ssrLoadModule("/src/features/data/hooks.ts"));
});

after(async () => {
  await vite?.close();
});

function queryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
}

function render(element, client, { url = "/", path = "/", adapter = {} } = {}) {
  return renderToStaticMarkup(
      h(
          MantineProvider,
          null,
          h(
              QueryClientProvider,
              { client },
              h(
                  SessionContext.Provider,
                  { value: { mode: "api", adapter } },
                  h(
                      MemoryRouter,
                      { initialEntries: [url] },
                      h(Routes, null, h(Route, { path, element })),
                  ),
              ),
          ),
      ),
  );
}

const ready = {
  id: "on-page",
  name: "Visible source",
  createdAtUtc: "2026-11-26T00:00:00Z",
  currentVersion: {
    id: "version",
    versionNumber: 1,
    originalFileName: "source.csv",
    rowCount: 7,
    columnCount: 2,
  },
};
const summary = { totalCount: 125, readyCount: 110, totalRows: 850000 };

test("DataPage reads search, readiness and pagination from URL and displays global statistics", async () => {
  const client = queryClient();
  const calls = [];
  const page = {
    items: [ready],
    page: 2,
    pageSize: 48,
    totalCount: 55,
    totalPages: 2,
  };
  const key = queryKeys.datasets.page("project", 2, 48, "Łódź", true);
  client.setQueryData(key, page);
  client.setQueryData(queryKeys.datasets.summary("project"), summary);
  try {
    const html = render(h(DataPage), client, {
      path: "/projects/:projectId/data",
      url: "/projects/project/data?page=2&pageSize=48&search=%20Łódź%20&readyOnly=true",
      adapter: {
        listDatasets: async (...args) => {
          calls.push(args);
          return page;
        },
      },
    });
    assert.match(html, /Matching datasets/);
    assert.match(html, /Visible source/);
    assert.match(html, /850,000/);
    assert.match(html, /across your project/);
    assert.match(html, /48 per page/);
    assert.doesNotMatch(html, /rows on this page|Filters the current page/);
    const query = client.getQueryCache().find({ queryKey: key, exact: true });
    await query.options.queryFn();
    assert.deepEqual(calls, [
      ["project", { page: 2, pageSize: 48, search: "Łódź", readyOnly: true }],
    ]);
  } finally {
    client.clear();
  }
});

test("empty search results keep search and clear-filter controls available", () => {
  const client = queryClient();
  client.setQueryData(
      queryKeys.datasets.page("project", 1, 24, "missing", true),
      {
        items: [],
        page: 1,
        pageSize: 24,
        totalCount: 0,
        totalPages: 1,
      },
  );
  client.setQueryData(queryKeys.datasets.summary("project"), summary);
  try {
    const html = render(h(DataPage), client, {
      path: "/projects/:projectId/data",
      url: "/projects/project/data?search=missing&readyOnly=true",
    });
    assert.match(html, /No datasets found/);
    assert.match(html, /aria-label="Search project datasets"/);
    assert.match(html, /value="missing"/);
    assert.match(html, /Clear filters/);
    assert.doesNotMatch(html, /Import your first dataset/);
  } finally {
    client.clear();
  }
});

test("source selector uses ready-only pages and loads an off-page selection by ID", async () => {
  const client = queryClient();
  const selected = {
    ...ready,
    id: "off-page",
    name: "Previously chosen source",
  };
  const calls = [];
  const page = {
    items: [ready],
    page: 1,
    pageSize: 24,
    totalCount: 25,
    totalPages: 2,
  };
  const key = queryKeys.datasets.page("project", 1, 24, "", true);
  const detailKey = queryKeys.datasets.detail("project", selected.id);
  client.setQueryData(key, page);
  client.setQueryData(detailKey, selected);
  try {
    const html = render(
        h(DatasetSelect, {
          projectId: "project",
          value: selected.id,
          onChange() {},
        }),
        client,
        {
          adapter: {
            listDatasets: async (...args) => {
              calls.push(["list", ...args]);
              return page;
            },
            getDataset: async (...args) => {
              calls.push(["detail", ...args]);
              return selected;
            },
          },
        },
    );
    assert.match(html, /value="Previously chosen source"/);
    assert.match(html, /value="off-page"/);
    assert.match(html, /Dataset source pages/);
    assert.doesNotMatch(html, /displayed options only/);
    await client
        .getQueryCache()
        .find({ queryKey: key, exact: true })
        .options.queryFn();
    await client
        .getQueryCache()
        .find({ queryKey: detailKey, exact: true })
        .options.queryFn();
    assert.deepEqual(calls, [
      [
        "list",
        "project",
        { page: 1, pageSize: 24, search: "", readyOnly: true },
      ],
      ["detail", "project", "off-page"],
    ]);
  } finally {
    client.clear();
  }
});

test("source selector preserves a previously selected source that is not ready", () => {
  const client = queryClient();
  const selected = {
    ...ready,
    id: "pending-source",
    name: "Pending source",
    currentVersion: null,
  };
  client.setQueryData(queryKeys.datasets.page("project", 1, 24, "", true), {
    items: [],
    page: 1,
    pageSize: 24,
    totalCount: 0,
    totalPages: 1,
  });
  client.setQueryData(
      queryKeys.datasets.detail("project", selected.id),
      selected,
  );
  try {
    const html = render(
        h(DatasetSelect, {
          projectId: "project",
          value: selected.id,
          onChange() {},
        }),
        client,
    );
    assert.match(html, /value="Pending source \(not ready\)"/);
    assert.match(html, /value="pending-source"/);
    assert.match(html, /No ready datasets are available/);
  } finally {
    client.clear();
  }
});

test("project overview metrics come from summary even when only one dataset is loaded", () => {
  const client = queryClient();
  client.setQueryData(queryKeys.datasets.page("project", 1, 24), {
    items: [ready],
    page: 1,
    pageSize: 24,
    totalCount: 125,
    totalPages: 6,
  });
  client.setQueryData(queryKeys.datasets.summary("project"), summary);
  client.setQueryData(queryKeys.dashboards.page("project", 1, 4), {
    items: [],
    page: 1,
    pageSize: 4,
    totalCount: 0,
    totalPages: 1,
  });
  try {
    const html = render(h(ProjectOverviewPage), client, {
      path: "/projects/:projectId",
      url: "/projects/project",
    });
    assert.match(html, /<strong>125<\/strong>/);
    assert.match(html, /110 ready to work with/);
    assert.ok(html.includes(summary.totalRows.toLocaleString()));
    assert.match(html, /Across all ready datasets/);
    assert.doesNotMatch(html, /loaded page|rows on this page/);
  } finally {
    client.clear();
  }
});

test("import and delete hooks invalidate every filtered page and project summary", async () => {
  const client = queryClient();
  const keys = [
    queryKeys.datasets.page("project", 1, 24),
    queryKeys.datasets.page("project", 2, 48, "sales", true),
    queryKeys.datasets.page("project", 1, 24, "revenue", false),
    queryKeys.datasets.summary("project"),
  ];
  const otherKey = queryKeys.datasets.summary("other-project");
  client.setQueryData(otherKey, summary);
  let operations;
  function Mutations() {
    operations = {
      import: useImportDataset("project"),
      remove: useDeleteDataset("project"),
    };
    return null;
  }
  const calls = [];
  try {
    render(h(Mutations), client, {
      adapter: {
        importCsv: async (projectId, file) => {
          calls.push(["import", projectId, file.name]);
          return { datasetId: "new-dataset" };
        },
        listDashboards: async () => ({
          items: [],
          page: 1,
          pageSize: 24,
          totalCount: 0,
          totalPages: 1,
        }),
        deleteDataset: async (projectId, datasetId) => {
          calls.push(["delete", projectId, datasetId]);
        },
      },
    });
    for (const key of keys) client.setQueryData(key, {});
    await operations.import.mutateAsync(new File(["a,b\n1,2"], "sales.csv"));
    for (const key of keys)
      assert.equal(client.getQueryState(key).isInvalidated, true);

    for (const key of keys) client.setQueryData(key, {});
    const detailKey = queryKeys.datasets.detail("project", "new-dataset");
    client.setQueryData(detailKey, ready);
    await operations.remove.mutateAsync("new-dataset");
    for (const key of keys)
      assert.equal(client.getQueryState(key).isInvalidated, true);
    assert.equal(client.getQueryData(detailKey), undefined);
    assert.equal(client.getQueryState(otherKey).isInvalidated, false);
    assert.deepEqual(calls, [
      ["import", "project", "sales.csv"],
      ["delete", "project", "new-dataset"],
    ]);
  } finally {
    client.clear();
  }
});

test("project and dashboard browsers use URL pages and server totals", async () => {
  const client = queryClient();
  const project = {
    id: "off-page",
    name: "Second page project",
    createdAtUtc: "2026-11-26T00:00:00Z",
  };
  const dashboard = {
    id: "board",
    projectId: project.id,
    name: "Second page dashboard",
    createdAtUtc: project.createdAtUtc,
  };
  const metadata = { page: 2, pageSize: 24, totalCount: 25, totalPages: 2 };
  client.setQueryData(queryKeys.projects.page(2, 24), {
    ...metadata,
    items: [project],
  });
  client.setQueryData(queryKeys.datasets.summary(project.id), summary);
  client.setQueryData(queryKeys.dashboards.page(project.id, 1, 1), {
    ...metadata,
    page: 1,
    pageSize: 1,
    totalPages: 25,
    items: [dashboard],
  });
  client.setQueryData(queryKeys.dashboards.page(project.id, 2, 24), {
    ...metadata,
    items: [dashboard],
  });
  try {
    const projects = render(h(ProjectsPage), client, {
      path: "/projects",
      url: "/projects?page=2&pageSize=24",
    });
    assert.match(projects, /Second page project/);
    assert.match(projects, /25 projects/);
    assert.match(projects, /Project pages/);
    assert.match(projects, /Projects per page/);
    assert.match(projects, /Filter and sort apply to this page only/);
    assert.match(projects, /25 dashboards/);
    const dashboards = render(h(DashboardsPage), client, {
      path: "/projects/:projectId/dashboards",
      url: "/projects/off-page/dashboards?page=2&pageSize=24",
    });
    assert.match(dashboards, /Second page dashboard/);
    assert.match(dashboards, /25 dashboards/);
    assert.match(dashboards, /Dashboard pages/);
    assert.match(dashboards, /Dashboards per page/);
    assert.match(dashboards, /Filters dashboards on the current page only/);
  } finally {
    client.clear();
  }
});

test("navigation preserves the current project outside the recent page and displays the total", () => {
  const client = queryClient();
  const recent = {
    id: "recent",
    name: "Recent project",
    createdAtUtc: "2026-11-26T00:00:00Z",
  };
  const current = { ...recent, id: "current", name: "Older selected project" };
  client.setQueryData(queryKeys.projects.page(1, 5), {
    items: [recent],
    page: 1,
    pageSize: 5,
    totalCount: 125,
    totalPages: 25,
  });
  client.setQueryData(queryKeys.projects.detail(current.id), current);
  try {
    const html = render(h(AppLayout), client, {
      path: "/projects/:projectId",
      url: "/projects/current",
    });
    assert.match(html, /Older selected project/);
    assert.match(html, /Recent project/);
    assert.match(html, /nav-count">125</);
    assert.match(html, /Recent and current projects only/);
    assert.equal(
        client.getQueryState(queryKeys.projects.page(1, 24)),
        undefined,
    );
  } finally {
    client.clear();
  }
});

test("project and dashboard mutations invalidate every page without mixing collections", async () => {
  const client = queryClient();
  const project = {
    id: "project",
    name: "Project",
    createdAtUtc: "2026-11-26T00:00:00Z",
  };
  const dashboard = {
    id: "dashboard",
    projectId: project.id,
    name: "Dashboard",
    createdAtUtc: project.createdAtUtc,
    revision: 7,
  };
  const projectKeys = [
    queryKeys.projects.page(1, 24),
    queryKeys.projects.page(2, 48),
    queryKeys.projects.page(1, 5),
  ];
  const dashboardKeys = [
    queryKeys.dashboards.page(project.id, 1, 24),
    queryKeys.dashboards.page(project.id, 2, 48),
    queryKeys.dashboards.page(project.id, 1, 1),
  ];
  const otherKey = queryKeys.dashboards.page("other-project", 1, 24);
  client.setQueryData(otherKey, {});
  let operations;
  function Mutations() {
    operations = {
      createProject: useCreateProject(),
      renameProject: useRenameProject(),
      deleteProject: useDeleteProject(),
      createDashboard: useCreateDashboard(project.id),
      deleteDashboard: useDeleteDashboard(project.id),
    };
    return null;
  }
  function seed(keys) {
    for (const key of keys) client.setQueryData(key, {});
  }
  function invalidated(keys) {
    for (const key of keys)
      assert.equal(client.getQueryState(key).isInvalidated, true);
  }
  try {
    render(h(Mutations), client, {
      adapter: {
        createProject: async () => project,
        renameProject: async (_, name) => ({ ...project, name }),
        deleteProject: async () => {},
        createDashboard: async () => dashboard,
        deleteDashboard: async () => {},
      },
    });
    seed(projectKeys);
    await operations.createProject.mutateAsync({
      name: "Project",
      description: "",
    });
    invalidated(projectKeys);
    seed(projectKeys);
    client.setQueryData(queryKeys.projects.detail(project.id), project);
    await operations.renameProject.mutateAsync({
      projectId: project.id,
      name: "Renamed",
    });
    invalidated(projectKeys);
    assert.equal(
        client.getQueryState(queryKeys.projects.detail(project.id)).isInvalidated,
        true,
    );

    seed(dashboardKeys);
    await operations.createDashboard.mutateAsync("Dashboard");
    invalidated(dashboardKeys);
    assert.deepEqual(
        client.getQueryData(
            queryKeys.dashboards.detail(project.id, dashboard.id),
        ),
        dashboard,
    );
    seed(dashboardKeys);
    client.setQueryData(
        queryKeys.dashboards.widgets(project.id, dashboard.id),
        [],
    );
    client.setQueryData(
        queryKeys.dashboards.editorState(project.id, dashboard.id),
        { dashboard },
    );
    await operations.deleteDashboard.mutateAsync(dashboard.id);
    invalidated(dashboardKeys);
    assert.equal(
        client.getQueryData(
            queryKeys.dashboards.detail(project.id, dashboard.id),
        ),
        undefined,
    );
    assert.equal(
        client.getQueryData(
            queryKeys.dashboards.widgets(project.id, dashboard.id),
        ),
        undefined,
    );
    assert.equal(
        client.getQueryData(
            queryKeys.dashboards.editorState(project.id, dashboard.id),
        ),
        undefined,
    );
    assert.equal(client.getQueryState(otherKey).isInvalidated, false);

    seed(projectKeys);
    await operations.deleteProject.mutateAsync(project.id);
    invalidated(projectKeys);
    assert.equal(
        client.getQueryData(queryKeys.projects.detail(project.id)),
        undefined,
    );
    assert.equal(client.getQueryData(dashboardKeys[0]), undefined);
    assert.ok(client.getQueryState(otherKey));
  } finally {
    client.clear();
  }
});

test("delete dataset skips usage scans and invalidates dependent caches", async () => {
  const client = queryClient();
  const calls = [];
  let remove;
  function Mutations() {
    remove = useDeleteDataset("project");
    return null;
  }
  const otherKey = queryKeys.dashboards.editorState(
      "other-project",
      "other-board",
  );
  const staleKeys = [
    queryKeys.datasets.page("project", 1, 24),
    queryKeys.datasets.page("project", 2, 48, "sales", true),
    queryKeys.datasets.summary("project"),
    queryKeys.dashboards.detail("project", "board"),
    queryKeys.dashboards.editorState("project", "board"),
    queryKeys.dashboards.widgets("project", "board"),
    queryKeys.dashboards.widgetData("project", "board", "widget"),
  ];
  const removedKeys = [
    queryKeys.datasets.detail("project", "source"),
    queryKeys.datasets.profile("project", "source"),
    queryKeys.datasets.importStatus("project", "source"),
    queryKeys.datasets.rows("project", "source", 1, 24),
    queryKeys.datasets.preparedPreview("project", "source", []),
    queryKeys.analytics.chartData("project", "source", {}),
    queryKeys.analytics.exploration("project", "source", "version", {}),
  ];
  try {
    render(h(Mutations), client, {
      adapter: {
        listDashboards: async () => {
          throw new Error("Unexpected dashboard scan");
        },
        listWidgets: async () => {
          throw new Error("Unexpected widget scan");
        },
        deleteDataset: async (...args) => {
          calls.push(args);
        },
      },
    });
    for (const key of [...staleKeys, ...removedKeys, otherKey]) {
      client.setQueryData(key, {});
    }

    await remove.mutateAsync("source");

    assert.deepEqual(calls, [["project", "source"]]);
    for (const key of staleKeys) {
      assert.equal(client.getQueryState(key)?.isInvalidated, true);
    }
    for (const key of removedKeys) {
      assert.equal(client.getQueryData(key), undefined);
    }
    assert.equal(client.getQueryState(otherKey)?.isInvalidated, false);
  } finally {
    client.clear();
  }
});

test("demo deletion detaches widgets and increments dashboard revisions", async () => {
  const state = createSeed();
  const projectId = state.projects[0].id;
  const dataset = state.datasets.find((item) => item.projectId === projectId);
  assert.ok(dataset);
  const datasetId = dataset.dataset.id;
  const sourceDashboard = state.dashboards.find((d) => d.projectId === projectId);
  assert.ok(sourceDashboard);
  const target = {
    ...sourceDashboard,
    id: crypto.randomUUID(),
    name: "Deletion test dashboard",
    revision: 5,
  };
  state.dashboards.push(target);
  const originalRevision = target.revision;
  const original = {
    id: crypto.randomUUID(),
    dashboardId: target.id,
    datasetId,
    type: "Kpi",
    title: "First chart",
    groupByColumn: "region",
    measureColumn: "amount",
    aggregation: "Count",
    layout: { x: 0, y: 0, width: 6, height: 14 },
    createdAtUtc: new Date().toISOString(),
  };
  const secondWidget = {
    ...original,
    id: crypto.randomUUID(),
    title: "Second chart",
    layout: { x: 6, y: 0, width: 6, height: 14 },
  };
  state.widgets.push(original, secondWidget);
  const priorRevisions = new Map(
      state.dashboards.map((board) => [board.id, board.revision]),
  );
  const affected = new Set(
      state.widgets
          .filter((w) => w.datasetId === datasetId)
          .map((w) => w.dashboardId),
  );

  const previous = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
  const values = new Map([[DEMO_STORAGE_KEY, JSON.stringify(state)]]);
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: {
      getItem: (key) => values.get(key) ?? null,
      setItem: (key, value) => values.set(key, value),
    },
  });
  try {
    await demoAdapter.deleteDataset(projectId, datasetId);
    const after = await demoAdapter.getDashboardEditorState(
        projectId,
        target.id,
    );
    assert.equal(after.dashboard.revision, originalRevision + 1);
    const detached = after.widgets.filter((widget) =>
        [original.id, secondWidget.id].includes(widget.id),
    );
    assert.equal(detached.length, 2);
    for (const widget of detached) {
      assert.equal(widget.datasetId, null);
      const before = widget.id === original.id ? original : secondWidget;
      assert.equal(widget.title, before.title);
      assert.deepEqual(widget.layout, before.layout);
    }
    for (const board of state.dashboards) {
      if (board.projectId !== projectId) continue;
      const actual = await demoAdapter.getDashboard(projectId, board.id);
      assert.equal(
          actual.revision,
          priorRevisions.get(board.id) + (affected.has(board.id) ? 1 : 0),
      );
    }
    assert.equal(
        (await demoAdapter.getDatasetsSummary(projectId)).totalCount,
        state.datasets.filter((item) => item.projectId === projectId).length - 1,
    );

    const updates = after.widgets.map((widget) => ({
      widgetId: widget.id,
      datasetId: widget.datasetId,
      type: widget.type,
      title: widget.title,
      groupByColumn: widget.groupByColumn,
      measureColumn: widget.measureColumn,
      aggregation: widget.aggregation,
      layout: widget.layout,
    }));
    const saved = await demoAdapter.updateDashboardWidgets(
        projectId,
        target.id,
        updates,
        after.dashboard.revision,
    );
    assert.equal(saved.revision, originalRevision + 2);

    await assert.rejects(
        demoAdapter.updateDashboardWidgets(
            projectId,
            target.id,
            [{ ...updates[0], widgetId: crypto.randomUUID(), datasetId: null }],
            saved.revision,
        ),
        /needs a dataset/,
    );
    await assert.rejects(
        demoAdapter.updateDashboardWidgets(
            projectId,
            target.id,
            [{ ...updates[0], datasetId }],
            saved.revision,
        ),
        /Reconnecting/,
    );
  } finally {
    if (previous) Object.defineProperty(globalThis, "localStorage", previous);
    else delete globalThis.localStorage;
  }
});
