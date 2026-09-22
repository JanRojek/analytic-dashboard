import type { DataRow } from "../data/types.ts";
import { createSeed } from "./seed.ts";

export interface ParsedCsv {
  columns: string[];
  rows: DataRow[];
}

export function createSampleFile(): File {
  const { columns, rows } = createSeed().datasets[0];
  const escape = (value: string | null) =>
    value === null
      ? ""
      : /[",\r\n]/.test(value)
        ? `"${value.replaceAll('"', '""')}"`
        : value;
  const content = [
    columns.map(escape).join(","),
    ...rows.map((row) =>
      columns.map((column) => escape(row[column])).join(","),
    ),
  ].join("\r\n");
  return new File([content], "retail_sales_sample.csv", { type: "text/csv" });
}

export const sampleCsvFile = createSampleFile;

/** A deliberately bounded CSV reader for the local demo, including quoted newlines. */
export function parseCsv(source: string): ParsedCsv {
  const text = source.replace(/^\uFEFF/, "");
  if (!text.trim())
    throw new Error(
      "This CSV is empty. Include column headers and at least one data row.",
    );
  const firstLine = text.split(/\r?\n/, 1)[0];
  const delimiterCounts = new Map([
    [",", 0],
    [";", 0],
    ["\t", 0],
  ]);
  let inQuotes = false;
  for (let i = 0; i < firstLine.length; i++) {
    if (firstLine[i] === '"') {
      if (inQuotes && firstLine[i + 1] === '"') {
        i++;
        continue;
      }
      inQuotes = !inQuotes;
    } else if (!inQuotes && delimiterCounts.has(firstLine[i]))
      delimiterCounts.set(firstLine[i], delimiterCounts.get(firstLine[i])! + 1);
  }
  const delimiter = [...delimiterCounts].sort((a, b) => b[1] - a[1])[0][0];
  const matrix: string[][] = [];
  let row: string[] = [];
  let value = "";
  let quoted = false;
  let closedQuote = false;
  const endField = () => {
    row.push(value);
    value = "";
    closedQuote = false;
  };
  const endRow = () => {
    endField();
    if (row.length > 1 || row.some((cell) => cell !== "")) matrix.push(row);
    row = [];
  };
  for (let i = 0; i < text.length; i++) {
    const character = text[i];
    if (quoted) {
      if (character === '"') {
        if (text[i + 1] === '"') {
          value += '"';
          i++;
        } else {
          quoted = false;
          closedQuote = true;
        }
      } else value += character;
    } else if (character === delimiter) endField();
    else if (character === "\n" || character === "\r") {
      if (character === "\r" && text[i + 1] === "\n") i++;
      endRow();
    } else if (character === '"' && value.length === 0 && !closedQuote)
      quoted = true;
    else {
      if (closedQuote || character === '"')
        throw new Error(
          "A quoted CSV value is malformed. Check the quotation marks in your file.",
        );
      value += character;
    }
  }
  if (quoted)
    throw new Error(
      "A quoted CSV value is unfinished. Check the quotation marks in your file.",
    );
  if (value.length || row.length || closedQuote) endRow();
  const columns = matrix.shift()?.map((column) => column.trim()) ?? [];
  if (!columns.length || columns.some((column) => !column))
    throw new Error("Each column needs a name in the first row.");
  if (new Set(columns).size !== columns.length)
    throw new Error(
      "Column names must be unique. Rename duplicate headers and try again.",
    );
  if (!matrix.length)
    throw new Error("This CSV contains headers but no data rows.");
  if (matrix.length > 20_000)
    throw new Error(
      "The local demo supports up to 20,000 rows. Use the connected workspace for larger files.",
    );
  const rows = matrix.map((cells, index) => {
    if (cells.length !== columns.length)
      throw new Error(
        `Row ${index + 2} has ${cells.length} values; the header has ${columns.length} columns.`,
      );
    return Object.fromEntries(
      columns.map((column, i) => [column, cells[i] === "" ? null : cells[i]]),
    ) as DataRow;
  });
  return { columns, rows };
}
