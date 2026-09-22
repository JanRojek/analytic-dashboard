import type {
  ColumnProfile,
  DataRow,
  Dataset,
  DatasetProfile,
  QueryConfig,
  QueryItem,
  TransformStep,
} from "../data/types.ts";

export function inferType(values: (string | null)[]): ColumnProfile["type"] {
  const present = values.filter(
    (value): value is string => value !== null && value.trim() !== "",
  );
  if (
    present.length &&
    present.every((value) => Number.isFinite(Number(value)))
  )
    return "number";
  if (
    present.length &&
    present.every(
      (value) =>
        /^\d{4}-\d{2}-\d{2}(?:[T ].*)?$/.test(value) &&
        Number.isFinite(Date.parse(value)),
    )
  )
    return "date";
  return "text";
}

export function profileRows(
  dataset: Dataset,
  columns: string[],
  rows: DataRow[],
): DatasetProfile {
  return {
    id: dataset.id,
    name: dataset.name,
    originalFileName: dataset.currentVersion?.originalFileName ?? dataset.name,
    rowCount: rows.length,
    columnCount: columns.length,
    columns: columns.map((name) => {
      const values = rows.map((row) => row[name] ?? null);
      const type = inferType(values);
      const numbers =
        type === "number"
          ? values
              .filter((value) => value !== null && value.trim() !== "")
              .map(Number)
          : [];
      return {
        name,
        type,
        nullCount: values.filter((value) => value === null).length,
        min: numbers.length ? String(Math.min(...numbers)) : null,
        max: numbers.length ? String(Math.max(...numbers)) : null,
        avg: numbers.length
          ? Math.round(
              (numbers.reduce((a, b) => a + b, 0) / numbers.length) * 100,
            ) / 100
          : null,
      };
    }),
    previewRows: rows.slice(0, 10).map((row) => ({ ...row })),
  };
}

export function queryRows(
  columns: string[],
  rows: DataRow[],
  config: QueryConfig,
): QueryItem[] {
  const { groupByColumn, measureColumn, aggregation } = config;
  if (!columns.includes(groupByColumn) || !columns.includes(measureColumn))
    throw new Error(
      "Choose a grouping column and a measure from this dataset.",
    );
  if (!["Sum", "Average", "Min", "Max", "Count"].includes(aggregation))
    throw new Error("Choose a supported aggregation.");
  if (
    aggregation !== "Count" &&
    inferType(rows.map((row) => row[measureColumn])) !== "number"
  )
    throw new Error(
      "This aggregation needs a numeric measure. Choose a number column or use Count.",
    );
  const groups = new Map<string | null, number[]>();
  for (const row of rows) {
    const label = row[groupByColumn] ?? null;
    if (!groups.has(label)) groups.set(label, []);
    const value = row[measureColumn];
    if (value !== null && value !== undefined)
      groups.get(label)!.push(aggregation === "Count" ? 1 : Number(value));
  }
  return [...groups]
    .filter(([, values]) => aggregation === "Count" || values.length > 0)
    .map(([label, values]) => {
      const sum = values.reduce((a, b) => a + b, 0);
      const value =
        aggregation === "Count"
          ? values.length
          : aggregation === "Sum"
            ? sum
            : aggregation === "Average"
              ? sum / values.length
              : aggregation === "Min"
                ? Math.min(...values)
                : Math.max(...values);
      return { label, value };
    })
    .sort((a, b) =>
      a.label === null
        ? b.label === null
          ? 0
          : 1
        : b.label === null
          ? -1
          : a.label < b.label
            ? -1
            : a.label > b.label
              ? 1
              : 0,
    );
}

export function transformRows(
  columns: string[],
  originalRows: DataRow[],
  steps: TransformStep[],
): DataRow[] {
  let rows = originalRows.map((row) => ({ ...row }));
  for (const step of steps) {
    if (step.kind !== "deduplicate" && !columns.includes(step.column))
      throw new Error(`Column "${step.column}" is not in this dataset.`);
    if (step.kind === "trim")
      rows = rows.map((row) => ({
        ...row,
        [step.column]: row[step.column]?.trim() ?? null,
      }));
    else if (step.kind === "fill") {
      if (step.value === undefined || step.value === "")
        throw new Error(
          "Enter a replacement value before filling missing data.",
        );
      rows = rows.map((row) => ({
        ...row,
        [step.column]:
          row[step.column] === null || row[step.column]?.trim() === ""
            ? step.value!
            : row[step.column],
      }));
    } else if (step.kind === "drop-empty")
      rows = rows.filter(
        (row) => row[step.column] !== null && row[step.column]?.trim() !== "",
      );
    else if (step.kind === "deduplicate") {
      const seen = new Set<string>();
      rows = rows.filter((row) => {
        const key = JSON.stringify(columns.map((column) => row[column]));
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
      });
    } else throw new Error("This preparation step is not supported.");
  }
  return rows;
}
