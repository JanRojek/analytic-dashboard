import { useState } from "react";

import { useDatasets, useDatasetUsage, useDeleteDataset } from "../hooks";

import {
  ActionIcon,
  Alert,
  Button,
  Menu,
  Modal,
  TextInput,
} from "@mantine/core";

import { Link, useParams } from "react-router-dom";

import { Icon } from "../../../components/Icon";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";

import { ImportDataDialog } from "../components/ImportDataDialog";

import type { Dataset } from "../../../data/types";
import { number, date } from "../formatters";

export function DataPage() {
  const { projectId = "" } = useParams();

  const [importOpen, setImportOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [deleting, setDeleting] = useState<Dataset | null>(null);

  const datasets = useDatasets(projectId);
  const usage = useDatasetUsage(projectId, deleting?.id);
  const remove = useDeleteDataset(projectId);

  const visible =
    datasets.data?.filter((item) =>
      item.name.toLowerCase().includes(search.toLowerCase()),
    ) ?? [];

  const ready = datasets.data?.filter((item) => item.currentVersion) ?? [];

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
      {datasets.isPending ? (
        <LoadingState label="Loading datasets…" />
      ) : datasets.isError ? (
        <ErrorState
          error={datasets.error}
          retry={() => void datasets.refetch()}
        />
      ) : !datasets.data.length ? (
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
          <div className="data-list-heading">
            <div>
              <h2>
                All datasets <span>{datasets.data.length}</span>
              </h2>
              <p>
                {number.format(
                  ready.reduce(
                    (total, item) =>
                      total + (item.currentVersion?.rowCount ?? 0),
                    0,
                  ),
                )}{" "}
                rows across your project
              </p>
            </div>
            <TextInput
              placeholder="Find a dataset…"
              aria-label="Find a dataset"
              value={search}
              onChange={(event) => setSearch(event.currentTarget.value)}
              leftSection={<Icon name="search" size={16} />}
            />
          </div>
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
              {!visible.length && (
                <div className="data-no-results">
                  No datasets match “{search}”.{" "}
                  <button onClick={() => setSearch("")}>Clear search</button>
                </div>
              )}
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
          cannot be undone.
        </p>
        {usage.isPending && (
          <p className="data-dialog-description" role="status">
            Checking whether dashboards use this dataset…
          </p>
        )}
        {usage.isError && (
          <ErrorState error={usage.error} retry={() => void usage.refetch()} />
        )}
        {!!usage.data?.length && (
          <Alert
            color="orange"
            title="This dataset is still in use"
            icon={<Icon name="warning" />}
          >
            Remove its charts from these dashboards before deleting the dataset:
            <ul className="dataset-usage-list">
              {usage.data.map(({ dashboard, count }) => (
                <li key={dashboard.id}>
                  <Link
                    to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                  >
                    {dashboard.name}
                  </Link>{" "}
                  · {count} {count === 1 ? "chart" : "charts"}
                </li>
              ))}
            </ul>
          </Alert>
        )}
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
            disabled={
              usage.isPending ||
              usage.isError ||
              usage.isFetching ||
              !!usage.data?.length
            }
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
