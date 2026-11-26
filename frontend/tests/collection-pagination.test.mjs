import test from "node:test";
import assert from "node:assert/strict";
import { apiAdapter } from "../src/data/api.ts";
import { demoAdapter } from "../src/mocks/adapter.ts";
import { createSeed } from "../src/mocks/seed.ts";
import { DEMO_STORAGE_KEY } from "../src/mocks/store.ts";

async function withCollection(kind, count, action) {
  const state = createSeed();
  const project = state.projects[0];
  const source =
    kind === "projects"
      ? project
      : state.dashboards.find((item) => item.projectId === project.id);
  state[kind] = Array.from({ length: count }, (_, index) => ({
    ...source,
    id: `${kind}-${String(index).padStart(3, "0")}`,
    name: `Item ${index}`,
    createdAtUtc: "2026-11-26T00:00:00Z",
    ...(kind === "dashboards" ? { revision: index + 1 } : {}),
  }));
  const values = new Map([[DEMO_STORAGE_KEY, JSON.stringify(state)]]);
  const previous = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: {
      getItem: (key) => values.get(key) ?? null,
      setItem: (key, value) => values.set(key, value),
    },
  });
  const list = (params) =>
    kind === "projects"
      ? demoAdapter.listProjects(params)
      : demoAdapter.listDashboards(project.id, params);
  try {
    await action({ state, project, list });
  } finally {
    if (previous) Object.defineProperty(globalThis, "localStorage", previous);
    else delete globalThis.localStorage;
  }
}

for (const kind of ["projects", "dashboards"]) {
  for (const count of [0, 1, 24, 25, 121]) {
    test(`demo ${kind} pagination reports correct totals for ${count} items`, async () => {
      await withCollection(kind, count, async ({ list }) => {
        const result = await list();
        assert.equal(result.page, 1);
        assert.equal(result.pageSize, 24);
        assert.equal(result.totalCount, count);
        assert.equal(result.totalPages, Math.max(1, Math.ceil(count / 24)));
        assert.equal(result.items.length, Math.min(count, 24));
        assert.equal(
          new Set(result.items.map((item) => item.id)).size,
          result.items.length,
        );
      });
    });
  }

  test(`demo ${kind} pages are stable, disjoint and normalized`, async () => {
    await withCollection(kind, 121, async ({ list, state }) => {
      const first = await list({ pageSize: 96 });
      const second = await list({ page: 2, pageSize: 96 });
      assert.equal(first.items.length, 96);
      assert.equal(second.items.length, 25);
      assert.equal(second.items[0].id, `${kind}-096`);
      assert.ok(
        first.items.every(
          (item) => !second.items.some((other) => other.id === item.id),
        ),
      );
      assert.deepEqual(await list({ pageSize: 96 }), first);
      for (const page of [0, -1, 1.5, 3, 2147483648]) {
        assert.equal((await list({ page, pageSize: 96 })).page, 1);
      }
      for (const pageSize of [0, -1, 97, 100, 1.5, NaN, Infinity]) {
        assert.equal((await list({ pageSize })).pageSize, 24);
      }
      state[kind][100].createdAtUtc = "2027-01-01T00:00:00Z";
      localStorage.setItem(DEMO_STORAGE_KEY, JSON.stringify(state));
      const newest = await list();
      assert.deepEqual(
        newest.items.slice(0, 3).map((item) => item.id),
        [`${kind}-100`, `${kind}-000`, `${kind}-001`],
      );
    });
  });
}

test("off-page details remain accessible and dashboard lists do not invent a revision", async () => {
  await withCollection("projects", 121, async ({ list }) => {
    assert.ok((await list()).items.every((item) => item.id !== "projects-120"));
    assert.equal(
      (await demoAdapter.getProject("projects-120")).name,
      "Item 120",
    );
  });
  await withCollection("dashboards", 121, async ({ list, project, state }) => {
    const result = await list({ page: 2, pageSize: 96 });
    assert.ok(result.items.every((item) => !Object.hasOwn(item, "revision")));
    const detail = await demoAdapter.getDashboard(project.id, "dashboards-120");
    assert.equal(detail.revision, 121);
    assert.equal(detail.name, "Item 120");
    const otherProject = state.projects.find((item) => item.id !== project.id);
    assert.equal(
      (await demoAdapter.listDashboards(otherProject.id)).totalCount,
      0,
    );
    await assert.rejects(
      demoAdapter.getDashboard(otherProject.id, detail.id),
      /not found/,
    );
    await assert.rejects(
      demoAdapter.listDashboards("missing-project"),
      /not found/,
    );
  });
});

test("demo project and dashboard mutations update totals and off-page resources", async () => {
  await withCollection("projects", 25, async ({ list }) => {
    const created = await demoAdapter.createProject("New project");
    assert.equal((await list()).totalCount, 26);
    await demoAdapter.renameProject("projects-024", "Renamed off-page project");
    assert.equal(
      (await demoAdapter.getProject("projects-024")).name,
      "Renamed off-page project",
    );
    await demoAdapter.deleteProject(created.id);
    assert.equal((await list()).totalCount, 25);
    assert.equal(
      (await list({ page: 2 })).items[0].name,
      "Renamed off-page project",
    );
  });
  await withCollection("dashboards", 25, async ({ project, list }) => {
    const created = await demoAdapter.createDashboard(
      project.id,
      "New dashboard",
    );
    assert.equal((await list()).totalCount, 26);
    await demoAdapter.deleteDashboard(project.id, created.id);
    assert.equal((await list()).totalCount, 25);
    await demoAdapter.deleteDashboard(project.id, "dashboards-024");
    const normalized = await list({ page: 2 });
    assert.equal(normalized.page, 1);
    assert.equal(normalized.totalCount, 24);
    assert.equal(normalized.totalPages, 1);
  });
});

test("API collection adapters request one page and preserve actual JSON without a list revision", async () => {
  const previous = globalThis.fetch;
  const calls = [];
  const project = {
    id: "off-page-project",
    name: "Project",
    createdAtUtc: "2026-11-26T00:00:00Z",
  };
  const dashboard = {
    id: "off-page-dashboard",
    projectId: project.id,
    name: "Dashboard",
    createdAtUtc: project.createdAtUtc,
  };
  const page = { page: 1, pageSize: 24, totalCount: 121, totalPages: 6 };
  const dashboardPage = {
    items: [dashboard],
    page: 2,
    pageSize: 48,
    totalCount: 49,
    totalPages: 2,
  };
  globalThis.fetch = async (url, options) => {
    calls.push(url);
    assert.equal(options.credentials, "include");
    const body = url.includes("?")
      ? url.includes("dashboards")
        ? dashboardPage
        : { ...page, items: [project] }
      : url.includes("dashboards")
        ? { ...dashboard, revision: 7 }
        : project;
    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  };
  try {
    const projects = await apiAdapter.listProjects({ page: 99, pageSize: 100 });
    assert.deepEqual(projects, { ...page, items: [project] });
    const dashboards = await apiAdapter.listDashboards(project.id, {
      page: 2,
      pageSize: 48,
    });
    assert.deepEqual(dashboards, dashboardPage);
    assert.equal(Object.hasOwn(dashboards.items[0], "revision"), false);
    assert.deepEqual(await apiAdapter.getProject(project.id), project);
    assert.deepEqual(await apiAdapter.getDashboard(project.id, dashboard.id), {
      ...dashboard,
      revision: 7,
    });
    assert.deepEqual(calls, [
      "/api/projects?page=99&pageSize=24",
      "/api/projects/off-page-project/dashboards?page=2&pageSize=48",
      "/api/projects/off-page-project",
      "/api/projects/off-page-project/dashboards/off-page-dashboard",
    ]);
  } finally {
    globalThis.fetch = previous;
  }
});
