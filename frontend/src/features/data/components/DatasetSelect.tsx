import { useState } from "react";
import { Group, Pagination, Select } from "@mantine/core";

import { ErrorState } from "../../../components/Feedback";
import { useDataset, useDatasets } from "../hooks";

export function DatasetSelect({
  projectId,
  value,
  onChange,
  disabled = false,
}: {
  projectId: string;
  value: string | null;
  onChange: (datasetId: string) => void;
  disabled?: boolean;
}) {
  const [page, setPage] = useState(1);
  const datasets = useDatasets(projectId, { page });
  const selected = useDataset(projectId, value ?? "", Boolean(value));
  const items = datasets.data?.items ?? [];
  const options =
    selected.data && !items.some((item) => item.id === selected.data.id)
      ? [selected.data, ...items]
      : items;

  return (
    <>
      <Select
        label="Source dataset"
        placeholder={
          datasets.isPending ? "Loading datasets…" : "Choose a dataset"
        }
        description="Browse pages; the filter searches displayed options only. Only ready datasets can be selected."
        searchable
        data={options.map((dataset) => ({
          value: dataset.id,
          label: dataset.currentVersion
            ? dataset.name
            : `${dataset.name} (not ready)`,
          disabled: !dataset.currentVersion,
        }))}
        value={value}
        disabled={disabled || (datasets.isPending && !selected.data)}
        allowDeselect={false}
        onChange={(datasetId) => datasetId && onChange(datasetId)}
      />
      {datasets.isError && (
        <ErrorState
          error={datasets.error}
          retry={() => void datasets.refetch()}
        />
      )}
      {value && selected.isError && (
        <ErrorState
          error={selected.error}
          retry={() => void selected.refetch()}
        />
      )}
      {datasets.data?.totalCount === 0 && (
        <p className="analytics-note">
          Import a dataset into this project to choose a source.
        </p>
      )}
      {datasets.data && (
        <Group gap="xs" mt="xs">
          <span className="analytics-note">
            Page {datasets.data.page} of {datasets.data.totalPages} ·{" "}
            {datasets.data.totalCount} datasets
          </span>
          {datasets.data.totalPages > 1 && (
            <Pagination
              aria-label="Dataset source pages"
              value={datasets.data.page}
              total={datasets.data.totalPages}
              onChange={setPage}
              disabled={disabled || datasets.isFetching}
              size="xs"
              siblings={0}
            />
          )}
        </Group>
      )}
    </>
  );
}
