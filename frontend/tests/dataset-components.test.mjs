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

let vite;
let DataPage;
let DatasetSelect;
let ProjectOverviewPage;
let SessionContext;
let useImportDataset;
let useDeleteDataset;

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
  ({ useImportDataset, useDeleteDataset } = await vite.ssrLoadModule(
    "/src/features/data/hooks.ts",
  ));
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
  client.setQueryData(queryKeys.dashboards.list("project"), []);
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
        listDashboards: async () => [],
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
