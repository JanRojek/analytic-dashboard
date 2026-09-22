import test from "node:test";
import assert from "node:assert/strict";
import { parseCsv, createSampleFile } from "../src/mocks/csv.ts";
import {
  queryRows,
  profileRows,
  transformRows,
} from "../src/mocks/analytics.ts";
import {
  readDemoState,
  mutateDemoState,
  DEMO_STORAGE_KEY,
} from "../src/mocks/store.ts";
import { createSeed } from "../src/mocks/seed.ts";
import { demoAdapter } from "../src/mocks/adapter.ts";
import { mockPreparation } from "../src/mocks/preparation.ts";

function memoryStorage() {
  const items = new Map();
  return {
    getItem: (key) => items.get(key) ?? null,
    setItem: (key, value) => items.set(key, value),
  };
}

test("CSV preserves quoted separators, escaped quotes, newlines, BOM and missing cells", () => {
  const parsed = parseCsv(
    '\uFEFFname,note,amount\r\n"A, B","Line 1\nLine ""2""",12\r\nC,,\r\n,,',
  );
  assert.deepEqual(parsed.columns, ["name", "note", "amount"]);
  assert.deepEqual(parsed.rows, [
    { name: "A, B", note: 'Line 1\nLine "2"', amount: "12" },
    { name: "C", note: null, amount: null },
    { name: null, note: null, amount: null },
  ]);
  assert.deepEqual(parseCsv("country;amount\nPoland;42").rows, [
    { country: "Poland", amount: "42" },
  ]);
});

test("CSV rejects malformed, duplicate, empty and ragged inputs", () => {
  for (const invalid of [
    "",
    "name,amount",
    "name,name\nA,B",
    ",amount\nA,2",
    'name,amount\n"A,2',
    "name,amount\nA,2,3",
    'name,amount\n"A"bad,2',
  ]) {
    assert.throws(() => parseCsv(invalid), Error, invalid);
  }
});

test("sample CSV roundtrips the real fixture rows", async () => {
  const file = createSampleFile();
  const result = parseCsv(await file.text());
  assert.deepEqual(result.rows, createSeed().datasets[0].rows);
  assert.equal(result.rows.length, 240);
});

test("queries count non-null measures, omit empty sum groups, and keep null labels last", () => {
  const columns = ["region", "amount"];
  const rows = [
    { region: "B", amount: "10" },
    { region: "B", amount: "20" },
    { region: "B", amount: null },
    { region: "A", amount: null },
    { region: null, amount: "5" },
  ];
  const base = { groupByColumn: "region", measureColumn: "amount" };
  assert.deepEqual(
    queryRows(columns, rows, { ...base, aggregation: "Count" }),
    [
      { label: "A", value: 0 },
      { label: "B", value: 2 },
      { label: null, value: 1 },
    ],
  );
  assert.deepEqual(queryRows(columns, rows, { ...base, aggregation: "Sum" }), [
    { label: "B", value: 30 },
    { label: null, value: 5 },
  ]);
  assert.equal(
    queryRows(columns, rows, { ...base, aggregation: "Average" })[0].value,
    15,
  );
  assert.equal(
    queryRows(columns, rows, { ...base, aggregation: "Min" })[0].value,
    10,
  );
  assert.equal(
    queryRows(columns, rows, { ...base, aggregation: "Max" })[0].value,
    20,
  );
  assert.throws(
    () =>
      queryRows(columns, rows, {
        ...base,
        aggregation: "Sum",
        measureColumn: "region",
      }),
    /numeric/,
  );
});

test("profiles derive completeness and numeric statistics from all rows", () => {
  const source = createSeed().datasets[0];
  const profile = profileRows(source.dataset, source.columns, source.rows);
  const region = profile.columns.find((column) => column.name === "region");
  const revenue = profile.columns.find((column) => column.name === "revenue");
  assert.equal(region.nullCount, 7);
  assert.equal(revenue.type, "number");
  assert.equal(profile.rowCount, 240);
  assert.equal(profile.previewRows.length, 10);
  assert.equal(
    Number(revenue.min),
    Math.min(...source.rows.map((row) => Number(row.revenue))),
  );
});

test("preparation is ordered and never mutates the original rows", () => {
  const original = [
    { name: " A ", region: null },
    { name: "A", region: "Europe" },
    { name: "B", region: null },
  ];
  const recipe = [
    { id: "1", kind: "trim", column: "name" },
    { id: "2", kind: "fill", column: "region", value: "Europe" },
    { id: "3", kind: "deduplicate", column: "" },
  ];
  assert.deepEqual(transformRows(["name", "region"], original, recipe), [
    { name: "A", region: "Europe" },
    { name: "B", region: "Europe" },
  ]);
  assert.equal(original[0].name, " A ");
  assert.equal(
    transformRows(["name", "region"], original, [
      { id: "4", kind: "drop-empty", column: "region" },
    ]).length,
    1,
  );
  assert.throws(
    () =>
      transformRows(["name", "region"], original, [
        { id: "5", kind: "trim", column: "missing" },
      ]),
    /not in this dataset/,
  );
});

test("demo mutations persist across reads and failed writes retain the old state", () => {
  const storage = memoryStorage();
  mutateDemoState((state) => {
    state.projects[0].name = "Saved project";
  }, storage);
  assert.equal(readDemoState(storage).projects[0].name, "Saved project");
  const before = storage.getItem(DEMO_STORAGE_KEY);
  const fullStorage = {
    ...storage,
    setItem: () => {
      throw new Error("Quota exceeded");
    },
  };
  assert.throws(
    () =>
      mutateDemoState((state) => {
        state.projects[0].name = "Unsaved project";
      }, fullStorage),
    /previous workspace is unchanged/,
  );
  assert.equal(storage.getItem(DEMO_STORAGE_KEY), before);
  assert.equal(readDemoState(storage).projects[0].name, "Saved project");
});

test("corrupt persisted data is reported without silently replacing user work", () => {
  const storage = memoryStorage();
  storage.setItem(DEMO_STORAGE_KEY, "{corrupt");
  assert.throws(() => readDemoState(storage), /could not be read/);
  assert.equal(storage.getItem(DEMO_STORAGE_KEY), "{corrupt");
});

test("a complete local journey persists imports, prepared datasets, dashboards and chart queries", async () => {
  const previous = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: memoryStorage(),
  });
  try {
    const project = await demoAdapter.createProject(
      "Portfolio workspace",
      "Created during the journey test",
    );
    const imported = await demoAdapter.importCsv(
      project.id,
      createSampleFile(),
    );
    const profile = await demoAdapter.getProfile(
      project.id,
      imported.datasetId,
    );
    assert.equal(profile.rowCount, 240);
    const steps = [
      {
        id: "fill-regions",
        kind: "fill",
        column: "region",
        value: "Unassigned",
      },
    ];
    mockPreparation.saveRecipe(imported.datasetId, steps);
    assert.deepEqual(mockPreparation.getRecipe(imported.datasetId), steps);
    const prepared = await mockPreparation.applyRecipe(
      project.id,
      imported.datasetId,
      steps,
    );
    assert.notEqual(prepared.id, imported.datasetId);
    assert.equal(
      (await demoAdapter.getProfile(project.id, prepared.id)).columns.find(
        (column) => column.name === "region",
      ).nullCount,
      0,
    );
    assert.equal(
      (
        await demoAdapter.getProfile(project.id, imported.datasetId)
      ).columns.find((column) => column.name === "region").nullCount,
      7,
    );
    const dashboard = await demoAdapter.createDashboard(
      project.id,
      "A new perspective",
    );
    const widget = await demoAdapter.createWidget(project.id, dashboard.id, {
      datasetId: prepared.id,
      type: "BarChart",
      title: "Revenue by region",
      groupByColumn: "region",
      measureColumn: "revenue",
      aggregation: "Sum",
    });
    const chartData = await demoAdapter.widgetData(
      project.id,
      dashboard.id,
      widget.id,
    );
    assert.ok(
      chartData.some((item) => item.label === "Unassigned" && item.value > 0),
    );
    assert.equal(
      (await demoAdapter.listWidgets(project.id, dashboard.id))[0].id,
      widget.id,
    );
    await assert.rejects(
      demoAdapter.getDataset("p-retail", prepared.id),
      /not found/,
    );
    await assert.rejects(
      demoAdapter.deleteDataset(project.id, prepared.id),
      /used by a dashboard/,
    );
    await demoAdapter.deleteWidget(project.id, dashboard.id, widget.id);
    await demoAdapter.deleteDataset(project.id, prepared.id);
    assert.equal((await demoAdapter.listDatasets(project.id)).length, 1);
    await demoAdapter.deleteProject(project.id);
    await assert.rejects(demoAdapter.getProject(project.id), /not found/);
  } finally {
    if (previous) Object.defineProperty(globalThis, "localStorage", previous);
    else delete globalThis.localStorage;
  }
});
