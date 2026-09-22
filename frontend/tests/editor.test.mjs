import test from "node:test";
import assert from "node:assert/strict";
import {
  editorScope,
  persistEditorDraft,
  readEditor,
  saveEditor,
  removeEditor,
  readExploration,
  saveExploration,
} from "../src/mocks/editor.ts";

function resetStorage() {
  const entries = new Map();
  globalThis.localStorage = {
    getItem: (key) => entries.get(key) ?? null,
    setItem: (key, value) => entries.set(key, value),
    removeItem: (key) => entries.delete(key),
  };
}

test("editor restores a draft without exposing it in the saved presentation", () => {
  resetStorage();
  const scope = editorScope("api", "owner", "project");
  const saved = {
    order: ["chart-a", "chart-b"],
    widths: { "chart-a": "half" },
    overrides: {},
  };
  saveEditor(scope, "dashboard", saved);
  const draft = {
    order: ["chart-b", "chart-a"],
    widths: { "chart-a": "full" },
    overrides: { "chart-a": { title: "New title", type: "LineChart" } },
  };
  persistEditorDraft(scope, "dashboard", draft);
  assert.deepEqual(readEditor(scope, "dashboard").document, draft);
  assert.equal(readEditor(scope, "dashboard").dirty, true);
  assert.deepEqual(
    readEditor(scope, "dashboard", false).document.order,
    saved.order,
  );
  assert.equal(
    readEditor(scope, "dashboard", false).document.widths["chart-a"],
    "half",
  );
  saveEditor(scope, "dashboard", draft);
  assert.equal(readEditor(scope, "dashboard").dirty, false);
  assert.deepEqual(
    readEditor(scope, "dashboard", false).document.order,
    draft.order,
  );
  assert.equal(
    readEditor(scope, "dashboard", false).document.overrides["chart-a"].title,
    "New title",
  );
  assert.ok(readEditor(scope, "dashboard").document.savedAt);
});

test("browser edits remain isolated by mode, account, project and dashboard", () => {
  resetStorage();
  const scope = editorScope("demo", "owner", "project");
  const draft = { order: ["chart"], widths: {}, overrides: {} };
  persistEditorDraft(scope, "dashboard", draft);
  for (const isolated of [
    editorScope("api", "owner", "project"),
    editorScope("demo", "other", "project"),
    editorScope("demo", "owner", "other"),
  ]) {
    assert.deepEqual(readEditor(isolated, "dashboard").document.order, []);
  }
  assert.deepEqual(readEditor(scope, "other-dashboard").document.order, []);
  removeEditor(scope, "dashboard");
  assert.deepEqual(readEditor(scope, "dashboard").document.order, []);
});

test("a damaged draft falls back to saved work and malformed overrides cannot replace widget identity", () => {
  resetStorage();
  const scope = editorScope("demo", "owner", "project");
  saveEditor(scope, "dashboard", {
    order: ["chart"],
    widths: {},
    overrides: { chart: { title: "Saved title" } },
  });
  localStorage.setItem(`${scope}:editor:dashboard:draft`, "{broken");
  assert.equal(
    readEditor(scope, "dashboard").document.overrides.chart.title,
    "Saved title",
  );
  assert.equal(readEditor(scope, "dashboard").dirty, false);
  localStorage.setItem(
    `${scope}:editor:dashboard:draft`,
    JSON.stringify({
      order: ["chart", "chart"],
      widths: {},
      overrides: {
        chart: {
          id: "different-chart",
          dashboardId: "different-dashboard",
          title: "Draft title",
        },
      },
    }),
  );
  const restored = readEditor(scope, "dashboard").document;
  assert.deepEqual(restored.order, ["chart"]);
  assert.deepEqual(restored.overrides.chart, { title: "Draft title" });
});

test("storage failures are raised to the editor rather than reported as successful saves", () => {
  resetStorage();
  localStorage.setItem = () => {
    throw new Error("Storage full");
  };
  const draft = { order: [], widths: {}, overrides: {} };
  assert.throws(() => saveEditor("scope", "dashboard", draft), /Storage full/);
  assert.throws(
    () => persistEditorDraft("scope", "dashboard", draft),
    /Storage full/,
  );
});

test("query drafts preserve configuration and reject malformed stored values", () => {
  resetStorage();
  const draft = {
    datasetId: "dataset",
    groupByColumn: "region",
    measureColumn: "revenue",
    aggregation: "Average",
    type: "BarChart",
    title: "Revenue by region",
  };
  saveExploration("scope", draft);
  assert.deepEqual(readExploration("scope"), draft);
  localStorage.setItem(
    "scope:exploration",
    JSON.stringify({ ...draft, aggregation: "Unknown" }),
  );
  assert.equal(readExploration("scope"), null);
  localStorage.setItem(
    "scope:exploration",
    JSON.stringify({ ...draft, title: null }),
  );
  assert.equal(readExploration("scope"), null);
});
