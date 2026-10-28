import type { Aggregation, WidgetType } from "../../data/types";

import { chartTypes } from "./chartConfig";

export interface DraftWidget {
  id: string;
  type: WidgetType;
  title: string;
  datasetId: string | null;
  groupByColumn: string | null;
  measureColumn: string | null;
  aggregation: Aggregation | null;
}

export function createDraftWidget(type: WidgetType): DraftWidget {
  return {
    id: `draft-${crypto.randomUUID()}`,
    type,
    title: "",
    datasetId: null,
    groupByColumn: null,
    measureColumn: null,
    aggregation: null,
  };
}

const validAggregations = new Set([
  "Sum",
  "Average",
  "Min",
  "Max",
  "Count",
]);

function isNullableString(value: unknown): boolean {
  return value === null || typeof value === "string";
}

export function readDraftWidgets(key: string): DraftWidget[] {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return [];

    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];

    return parsed.filter(
        (draft): draft is DraftWidget =>
            typeof draft === "object" &&
            draft !== null &&
            typeof draft.id === "string" &&
            typeof draft.title === "string" &&
            chartTypes.some((type) => type.value === draft.type) &&
            isNullableString(draft.datasetId) &&
            isNullableString(draft.groupByColumn) &&
            isNullableString(draft.measureColumn) &&
            (
                draft.aggregation === null ||
                (typeof draft.aggregation === "string" &&
                    validAggregations.has(draft.aggregation))
            )
    );
  } catch {
    return [];
  }
}
