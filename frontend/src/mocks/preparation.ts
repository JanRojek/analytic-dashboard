import type { Dataset, TransformStep } from "../data/types.ts";
import { profileRows, transformRows } from "./analytics.ts";
import { mutateDemoState, readDemoState, requireDataset } from "./store.ts";

export const mockPreparation = {
  getRecipe(datasetId: string): TransformStep[] {
    return readDemoState().recipes[datasetId] ?? [];
  },
  saveRecipe(datasetId: string, steps: TransformStep[]): void {
    mutateDemoState((state) => {
      if (!state.datasets.some((item) => item.dataset.id === datasetId))
        throw new Error("This dataset was not found.");
      state.recipes[datasetId] = structuredClone(steps);
    });
  },
  preview(projectId: string, datasetId: string, steps: TransformStep[]) {
    const source = requireDataset(readDemoState(), projectId, datasetId);
    return profileRows(
      source.dataset,
      source.columns,
      transformRows(source.columns, source.rows, steps),
    );
  },
  async applyRecipe(
    projectId: string,
    datasetId: string,
    steps: TransformStep[],
  ): Promise<Dataset> {
    if (!steps.length)
      throw new Error(
        "Add a preparation step before creating a prepared dataset.",
      );
    return mutateDemoState((state) => {
      const source = requireDataset(state, projectId, datasetId);
      const rows = transformRows(source.columns, source.rows, steps);
      if (!rows.length)
        throw new Error(
          "These steps would remove every row. Adjust the recipe before applying it.",
        );
      const dataset: Dataset = {
        id: crypto.randomUUID(),
        name: `${source.dataset.name.slice(0, 185)} · prepared`,
        createdAtUtc: new Date().toISOString(),
        currentVersion: {
          id: crypto.randomUUID(),
          versionNumber: 1,
          originalFileName:
            source.dataset.currentVersion?.originalFileName ?? "prepared.csv",
          rowCount: rows.length,
          columnCount: source.columns.length,
        },
      };
      state.datasets.unshift({
        projectId,
        dataset,
        columns: [...source.columns],
        rows,
      });
      state.recipes[datasetId] = structuredClone(steps);
      return dataset;
    });
  },
};
