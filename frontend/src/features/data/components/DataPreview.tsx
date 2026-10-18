import { useMemo, useState } from "react";
import { Pagination, Select, TextInput } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type {
  ColumnProfile,
  DataRow,
  DatasetProfile,
} from "../../../data/types";

import { number } from "../formatters";

const fieldIcon = (type: ColumnProfile["type"]) =>
  type === "number" ? "number" : type === "date" ? "calendar" : "text";

export function DataPreview({
  profile,
  rows: sourceRows,
  page,
  pageSize,
  totalPages,
  paginated,
  onPageChange,
  onPageSizeChange,
  title = "Data",
}: {
  profile: DatasetProfile;
  rows: DataRow[];
  page: number;
  pageSize: number;
  totalPages: number;
  paginated: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  title?: string;
}) {
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<{
    column: string;
    ascending: boolean;
  } | null>(null);
  const rows = useMemo(() => {
    const filtered = sourceRows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) =>
        Object.values(row).some((value) =>
          String(value ?? "")
            .toLowerCase()
            .includes(search.toLowerCase()),
        ),
      );
    if (!sort) return filtered;
    const type = profile.columns.find(
      (column) => column.name === sort.column,
    )?.type;
    return filtered.sort((a, b) => {
      const left = a.row[sort.column];
      const right = b.row[sort.column];
      const comparison =
        type === "number" && left !== null && right !== null
          ? Number(left) - Number(right)
          : String(left ?? "").localeCompare(String(right ?? ""));
      return sort.ascending ? comparison : -comparison;
    });
  }, [sourceRows, profile.columns, search, sort]);
  return (
    <section className="data-preview-card">
      <div className="data-preview-toolbar">
        <div>
          <h2>{title}</h2>
          <p>
            {number.format(profile.rowCount)} rows ·{" "}
            {number.format(profile.columnCount)} columns
          </p>
        </div>
        <TextInput
          placeholder="Search preview…"
          aria-label="Search preview rows"
          size="xs"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
          leftSection={<Icon name="search" size={14} />}
        />
      </div>
      <div
        className="data-table-scroll preview-scroll"
        tabIndex={0}
        aria-label="Dataset row preview"
      >
        <table className="data-table preview-table">
          <thead>
            <tr>
              <th className="row-number">#</th>
              {profile.columns.map((column) => (
                <th
                  key={column.name}
                  aria-sort={
                    sort?.column === column.name
                      ? sort.ascending
                        ? "ascending"
                        : "descending"
                      : "none"
                  }
                >
                  <button
                    className="preview-column-button"
                    onClick={() =>
                      setSort({
                        column: column.name,
                        ascending:
                          sort?.column === column.name ? !sort.ascending : true,
                      })
                    }
                  >
                    <Icon name={fieldIcon(column.type)} size={14} />
                    <span>{column.name}</span>
                    {sort?.column === column.name && (
                      <span aria-hidden="true">
                        {sort.ascending ? "↑" : "↓"}
                      </span>
                    )}
                  </button>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(({ row, index }) => (
              <tr key={index}>
                <td className="row-number">
                  {paginated ? (page - 1) * pageSize + index + 1 : index + 1}
                </td>
                {profile.columns.map((column) => (
                  <td
                    key={column.name}
                    className={column.type === "number" ? "numeric-cell" : ""}
                  >
                    {row[column.name] === null ||
                    row[column.name] === undefined ||
                    row[column.name] === "" ? (
                      <span className="null-value">empty</span>
                    ) : (
                      row[column.name]
                    )}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        {!rows.length && (
          <div className="data-no-results">
            {search
              ? "No preview rows match your search."
              : "This dataset has no rows."}
          </div>
        )}
      </div>
      <div className="data-preview-footer">
        <div className="data-preview-note">
          <Icon name="eye" size={14} />
          <span>Search and sorting apply to this page only.</span>
        </div>

        {paginated && (
          <div className="data-preview-controls">
            {totalPages > 1 && (
              <Pagination
                value={page}
                onChange={onPageChange}
                total={totalPages}
                size="sm"
              />
            )}

            <div className="data-page-size">
              <span>Rows per page</span>

              <Select
                size="xs"
                w={78}
                value={String(pageSize)}
                data={["25", "50", "100"]}
                onChange={(value) => {
                  if (value) {
                    onPageSizeChange(Number(value));
                  }
                }}
              />
            </div>
          </div>
        )}

        <span className="data-preview-columns">
          {profile.columnCount} columns
        </span>
      </div>
    </section>
  );
}
