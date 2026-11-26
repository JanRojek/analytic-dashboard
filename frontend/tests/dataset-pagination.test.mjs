import test from "node:test";
import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import { apiAdapter } from "../src/data/api.ts";
import { normalizePagination } from "../src/data/pagination.ts";
import { queryKeys } from "../src/data/queryKeys.ts";
import { demoAdapter } from "../src/mocks/adapter.ts";
import { createSeed } from "../src/mocks/seed.ts";
import { DEMO_STORAGE_KEY } from "../src/mocks/store.ts";

async function withDatasets(count, action) {
  const state = createSeed();
  const projectId = state.projects[0].id;
  const source = state.datasets.find((item) => item.projectId === projectId);
  state.datasets = Array.from({ length: count }, (_, index) => ({
    ...source,
    dataset: {
      ...source.dataset,
      id: `dataset-${String(index).padStart(3, "0")}`,
      name: `Dataset ${index}`,
      createdAtUtc: "2026-11-24T00:00:00Z",
    },
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
  try {
    await action(projectId, state);
  } finally {
    if (previous) Object.defineProperty(globalThis, "localStorage", previous);
    else delete globalThis.localStorage;
  }
}

for (const count of [0, 1, 24, 25, 121]) {
  test(`demo dataset pagination reports exact totals for ${count} datasets`, async () => {
    await withDatasets(count, async (projectId) => {
      const page = await demoAdapter.listDatasets(projectId);
      assert.equal(page.page, 1);
      assert.equal(page.pageSize, 24);
      assert.equal(page.totalCount, count);
      assert.equal(page.totalPages, Math.max(1, Math.ceil(count / 24)));
      assert.equal(page.items.length, Math.min(count, 24));
      assert.equal(
        new Set(page.items.map((item) => item.id)).size,
        page.items.length,
      );
    });
  });
}

test("demo pages are disjoint, deterministic, and selected details remain available off-page", async () => {
  await withDatasets(25, async (projectId) => {
    const first = await demoAdapter.listDatasets(projectId);
    const second = await demoAdapter.listDatasets(projectId, { page: 2 });
    assert.equal(second.page, 2);
    assert.equal(second.items.length, 1);
    assert.equal(second.items[0].id, "dataset-024");
    assert.ok(first.items.every((item) => item.id !== second.items[0].id));
    assert.deepEqual(await demoAdapter.listDatasets(projectId), first);
    assert.deepEqual(
      await demoAdapter.getDataset(projectId, second.items[0].id),
      second.items[0],
    );
    await assert.rejects(
      demoAdapter.getDataset(projectId, "missing-dataset"),
      /not found/,
    );
  });
});

test("normalization matches collection defaults and limits without changing valid sizes", async () => {
  assert.deepEqual(normalizePagination(), { page: 1, pageSize: 24 });
  for (const value of [0, -1, 97, 100, 1.5, NaN, Infinity]) {
    assert.equal(normalizePagination({ pageSize: value }).pageSize, 24);
  }
  for (const value of [1, 4, 24, 48, 96]) {
    assert.equal(normalizePagination({ pageSize: value }).pageSize, value);
  }
  for (const value of [0, -1, 1.5, NaN, Infinity, 2_147_483_648]) {
    assert.equal(normalizePagination({ page: value }).page, 1);
  }
  await withDatasets(121, async (projectId) => {
    const normalized = await demoAdapter.listDatasets(projectId, {
      page: -2,
      pageSize: 100,
    });
    assert.equal(normalized.page, 1);
    assert.equal(normalized.pageSize, 24);
    assert.equal(normalized.totalPages, 6);
    const last = await demoAdapter.listDatasets(projectId, {
      page: 2,
      pageSize: 96,
    });
    assert.equal(last.items.length, 25);
    assert.equal(last.totalPages, 2);
    const outOfRange = await demoAdapter.listDatasets(projectId, {
      page: 3,
      pageSize: 96,
    });
    assert.equal(outOfRange.page, 1);
    assert.equal(outOfRange.items.length, 96);
  });
});

test("demo dataset pages retain project isolation", async () => {
  await withDatasets(25, async (projectId, state) => {
    const otherProject = state.projects.find((item) => item.id !== projectId);
    const other = await demoAdapter.listDatasets(otherProject.id);
    assert.equal(other.totalCount, 0);
    assert.deepEqual(other.items, []);
    await assert.rejects(
      demoAdapter.getDataset(otherProject.id, "dataset-024"),
      /not found/,
    );
    await assert.rejects(
      demoAdapter.listDatasets("missing-project"),
      /not found/,
    );
  });
});

test("API requests one dataset page and preserves server normalization and off-page details", async () => {
  const previous = globalThis.fetch;
  const calls = [];
  const selected = {
    id: "off-page",
    name: "Selected source",
    currentVersion: { id: "version" },
  };
  const page = {
    items: [],
    page: 1,
    pageSize: 24,
    totalCount: 25,
    totalPages: 2,
  };
  globalThis.fetch = async (url) => {
    calls.push(url);
    return new Response(
      JSON.stringify(url.endsWith("/off-page") ? selected : page),
      {
        status: 200,
        headers: { "Content-Type": "application/json" },
      },
    );
  };
  try {
    assert.deepEqual(
      await apiAdapter.listDatasets("project", { page: 99, pageSize: 100 }),
      page,
    );
    assert.deepEqual(
      await apiAdapter.getDataset("project", selected.id),
      selected,
    );
    assert.deepEqual(calls, [
      "/api/projects/project/datasets?page=99&pageSize=24",
      "/api/projects/project/datasets/off-page",
    ]);
  } finally {
    globalThis.fetch = previous;
  }
});

test("page cache entries remain distinct and list invalidation reaches every page", async () => {
  const client = new QueryClient();
  try {
    const keys = [
      queryKeys.datasets.page("project", 1, 24),
      queryKeys.datasets.page("project", 2, 24),
      queryKeys.datasets.page("project", 1, 96),
    ];
    for (const [index, key] of keys.entries())
      client.setQueryData(key, { index });
    const otherKey = queryKeys.datasets.page("other-project", 1, 24);
    const detailKey = queryKeys.datasets.detail("project", "off-page");
    client.setQueryData(otherKey, {});
    client.setQueryData(detailKey, {});
    for (const [index, key] of keys.entries())
      assert.equal(client.getQueryData(key).index, index);
    await client.invalidateQueries({
      queryKey: queryKeys.datasets.list("project"),
      refetchType: "none",
    });
    for (const key of keys)
      assert.equal(client.getQueryState(key).isInvalidated, true);
    assert.equal(client.getQueryState(otherKey).isInvalidated, false);
    assert.equal(client.getQueryState(detailKey).isInvalidated, false);
  } finally {
    client.clear();
  }
});
