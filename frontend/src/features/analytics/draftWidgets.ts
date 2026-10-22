import type { Aggregation, WidgetType } from "../../data/types";

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
