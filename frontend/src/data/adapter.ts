import type { DataAdapter, DataMode } from "./types";
import { apiAdapter } from "./api";
import { demoAdapter } from "../mocks/adapter";

export function getAdapter(mode: DataMode): DataAdapter {
  return mode === "demo" ? demoAdapter : apiAdapter;
}
