import { useEffect, useState } from "react";

import {
  useDatasets,
  useDatasetsSummary,
  useDeleteDataset,
} from "../hooks";
import { useDebouncedValue } from "@mantine/hooks";

import {
  ActionIcon,
  Button,
  Checkbox,
  Menu,
  Modal,
  Pagination,
  Select,
  TextInput,
} from "@mantine/core";

import { Link, useParams, useSearchParams } from "react-router-dom";

import { Icon } from "../../../components/Icon";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";

import { ImportDataDialog } from "../components/ImportDataDialog";

import { normalizePagination } from "../../../data/pagination";

import type { Dataset } from "../../../data/types";
import { number, date } from "../formatters";

export function DataPage() {
  const { projectId = "" } = useParams();

  const [importOpen, setImportOpen] = useState(false);
  const [deleting, setDeleting] = useState<Dataset | null>(null);

  const [params, setParams] = useSearchParams();
  const pagination = normalizePagination({
    page: Number(params.get("page") ?? 1),
    pageSize: Number(params.get("pageSize") ?? 24),
  });
  const search = params.get("search") ?? "";
  const readyOnly = params.get("readyOnly") === "true";
  const [debouncedSearch] = useDebouncedValue(search, 300);
  const searching = search !== debouncedSearch;
  const datasets = useDatasets(
      projectId,
      { ...pagination, search: debouncedSearch, readyOnly },
      !searching,
  );
  const summary = useDatasetsSummary(projectId);
  const result = datasets.data;

  useEffect(() => {
    if (!result || searching || datasets.isFetching) return;
    if (
        params.get("page") === String(result.page) &&
        params.get("pageSize") === String(result.pageSize)
    )
      return;

    setParams(
        (current) => {
          const next = new URLSearchParams(current);
          next.set("page", String(result.page));
          next.set("pageSize", String(result.pageSize));
          return next;
        },
        { replace: true },
    );
  }, [result, params, setParams, searching, datasets.isFetching]);

  function changePage(
      page: number,
      pageSize = result?.pageSize ?? pagination.pageSize,
  ) {
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.set("page", String(page));
      next.set("pageSize", String(pageSize));
      return next;
    });
  }
  const remove = useDeleteDataset(projectId);

  function changeFilter(name: "search" | "readyOnly", value: string) {
    setParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (value) next.set(name, value);
          else next.delete(name);
          next.set("page", "1");
          return next;
        },
        { replace: true },
    );
  }

  function clearFilters() {
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.delete("search");
      next.delete("readyOnly");
      next.set("page", "1");
      return next;
    });
  }

  const filtered = Boolean(search.trim() || readyOnly);
  const visible = datasets.data?.items ?? [];

  return (
      <div className="page-content data-page">
        <PageHeading
            eyebrow="YOUR PROJECT'S FOUNDATION"
            title="Data"
            description="Bring your data together. Understand it before you build with it."
            actions={
              <Button
                  leftSection={<Icon name="plus" size={17} />}
                  onClick={() => setImportOpen(true)}
              >
                Add data
              </Button>
            }
        />
        <div className="data-workflow-banner">
          <div className="data-workflow-step">
            <span>01</span>
            <div>
              <strong>Bring it in</strong>
              <p>Turn a CSV into a dataset.</p>
            </div>
          </div>
          <Icon name="arrow" size={18} />
          <div className="data-workflow-step">
            <span>02</span>
            <div>
              <strong>Get to know it</strong>
              <p>Check columns and completeness.</p>
            </div>
          </div>
          <Icon name="arrow" size={18} />
          <div className="data-workflow-step">
            <span>03</span>
            <div>
              <strong>Find your story</strong>
              <p>Prepare, explore, and visualize.</p>
            </div>
          </div>
        </div>
        <div className="data-list-heading">
          <div>
            <h2>
              {filtered ? "Matching datasets" : "All datasets"}{" "}
              <span>{searching ? "…" : (datasets.data?.totalCount ?? "—")}</span>
            </h2>
            <p>
              {summary.data ? number.format(summary.data.totalRows) : "—"} rows
              across your project
            </p>
          </div>
          <div className="data-preview-controls">
            <TextInput
                placeholder="Search datasets…"
                aria-label="Search project datasets"
                value={search}
                onChange={(event) =>
                    changeFilter("search", event.currentTarget.value)
                }
                leftSection={<Icon name="search" size={16} />}
            />
            <Checkbox
                label="Ready only"
                checked={readyOnly}
                onChange={(event) =>
                    changeFilter(
                        "readyOnly",
                        event.currentTarget.checked ? "true" : "",
                    )
                }
            />
          </div>
        </div>
        {summary.isError && (
            <ErrorState
                error={summary.error}
                retry={() => void summary.refetch()}
            />
        )}
        {searching || datasets.isPending ? (
            <LoadingState label="Loading datasets…" />
        ) : datasets.isError ? (
            <ErrorState
                error={datasets.error}
                retry={() => void datasets.refetch()}
            />
        ) : !datasets.data.totalCount && filtered ? (
            <EmptyState
                icon="search"
                title="No datasets found"
                description="Try a different name or clear the filters."
                action={
                  <Button variant="default" onClick={clearFilters}>
                    Clear filters
                  </Button>
                }
            />
        ) : !datasets.data.totalCount ? (
            <EmptyState
                icon="data"
                title="Good questions start with data."
                description="Add your first CSV to inspect its columns, check data quality, and start finding patterns."
                action={
                  <Button
                      leftSection={<Icon name="upload" size={17} />}
                      onClick={() => setImportOpen(true)}
                  >
                    Import your first dataset
                  </Button>
                }
            />
        ) : (
            <>
              <div className="data-table-card">
                <div
                    className="data-table-scroll"
                    tabIndex={0}
                    aria-label="Project datasets"
                >
                  <table className="data-table datasets-table">
                    <thead>
                    <tr>
                      <th>Dataset</th>
                      <th>Rows</th>
                      <th>Columns</th>
                      <th>Status</th>
                      <th>Added</th>
                      <th>
                        <span className="sr-only">Actions</span>
                      </th>
                    </tr>
                    </thead>
                    <tbody>
                    {visible.map((dataset) => (
                        <tr key={dataset.id}>
                          <td>
                            <Link
                                to={`/projects/${projectId}/data/${dataset.id}`}
                                className="dataset-name-link"
                            >
                          <span className="data-file-symbol">
                            <Icon name="data" size={20} />
                          </span>
                              <span>
                            <strong>{dataset.name}</strong>
                            <small>
                              {dataset.currentVersion?.originalFileName ??
                                  "CSV import"}
                            </small>
                          </span>
                            </Link>
                          </td>
                          <td>
                            {dataset.currentVersion
                                ? number.format(dataset.currentVersion.rowCount)
                                : "—"}
                          </td>
                          <td>{dataset.currentVersion?.columnCount ?? "—"}</td>
                          <td>
                        <span
                            className={`data-status ${dataset.currentVersion ? "" : "pending"}`}
                        >
                          <i />
                          {dataset.currentVersion
                              ? "Ready to explore"
                              : "Not yet ready"}
                        </span>
                          </td>
                          <td className="data-muted">
                            {date(dataset.createdAtUtc)}
                          </td>
                          <td>
                            <Menu position="bottom-end" withinPortal>
                              <Menu.Target>
                                <ActionIcon
                                    variant="subtle"
                                    color="gray"
                                    aria-label={`Actions for ${dataset.name}`}
                                >
                                  <Icon name="more" />
                                </ActionIcon>
                              </Menu.Target>
                              <Menu.Dropdown>
                                <Menu.Item
                                    component={Link}
                                    to={`/projects/${projectId}/data/${dataset.id}`}
                                    leftSection={<Icon name="eye" size={16} />}
                                >
                                  Inspect dataset
                                </Menu.Item>
                                <Menu.Item
                                    component={Link}
                                    to={`/projects/${projectId}/explore?dataset=${dataset.id}`}
                                    disabled={!dataset.currentVersion}
                                    leftSection={<Icon name="chart" size={16} />}
                                >
                                  Build a visualization
                                </Menu.Item>
                                <Menu.Divider />
                                <Menu.Item
                                    color="red"
                                    leftSection={<Icon name="trash" size={16} />}
                                    onClick={() => {
                                      remove.reset();
                                      setDeleting(dataset);
                                    }}
                                >
                                  Delete dataset
                                </Menu.Item>
                              </Menu.Dropdown>
                            </Menu>
                          </td>
                        </tr>
                    ))}
                    </tbody>
                  </table>
                </div>
              </div>
              <div className="data-preview-footer">
            <span>
              Showing {(datasets.data.page - 1) * datasets.data.pageSize + 1}–
              {Math.min(
                  datasets.data.page * datasets.data.pageSize,
                  datasets.data.totalCount,
              )}{" "}
              of {datasets.data.totalCount} datasets
            </span>
                <div className="data-preview-controls">
                  {datasets.data.totalPages > 1 && (
                      <Pagination
                          value={datasets.data.page}
                          total={datasets.data.totalPages}
                          onChange={(page) => changePage(page)}
                          size="sm"
                      />
                  )}
                  <Select
                      aria-label="Datasets per page"
                      size="xs"
                      w={145}
                      value={String(datasets.data.pageSize)}
                      data={[...new Set([24, 48, 96, datasets.data.pageSize])]
                          .sort((a, b) => a - b)
                          .map((size) => ({
                            value: String(size),
                            label: `${size} per page`,
                          }))}
                      allowDeselect={false}
                      onChange={(value) => value && changePage(1, Number(value))}
                  />
                </div>
              </div>
              <p className="data-footnote">
                <Icon name="lock" size={14} /> Datasets belong to this project and
                can power multiple dashboards.
              </p>
            </>
        )}
        <ImportDataDialog
            projectId={projectId}
            opened={importOpen}
            onClose={() => setImportOpen(false)}
        />
        <Modal
            opened={!!deleting}
            onClose={() => {
              if (!remove.isPending) setDeleting(null);
            }}
            title="Delete this dataset?"
            centered
            closeOnClickOutside={!remove.isPending}
            closeOnEscape={!remove.isPending}
            withCloseButton={!remove.isPending}
        >
          <p className="data-dialog-description">
            “{deleting?.name}” and its data will be permanently removed. This
            cannot be undone. If dashboard widgets use this dataset, they will
            keep their layout and settings but lose their data source.
          </p>
          {remove.error && <ErrorState error={remove.error} />}
          <div className="dialog-footer">
            <Button
                variant="default"
                disabled={remove.isPending}
                onClick={() => setDeleting(null)}
            >
              Keep dataset
            </Button>
            <Button
                color="red"
                loading={remove.isPending}
                disabled={remove.isPending}
                onClick={() => {
                  if (deleting) {
                    remove.mutate(deleting.id, {
                      onSuccess: () => setDeleting(null),
                    });
                  }
                }}
            >
              Delete dataset
            </Button>
          </div>
        </Modal>
      </div>
  );
}
