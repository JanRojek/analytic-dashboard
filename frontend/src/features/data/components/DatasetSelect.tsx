import { useState } from "react";
import { Button, Group, Pagination, Select } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";

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
  const [search, setSearch] = useState("");
  const [debouncedSearch] = useDebouncedValue(search, 300);
  const searching = search !== debouncedSearch;
  const datasets = useDatasets(
    projectId,
    { page, search: debouncedSearch, readyOnly: true },
    !searching,
  );
  const selected = useDataset(projectId, value ?? "", Boolean(value));
  const items = searching ? [] : (datasets.data?.items ?? []);
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
        description="Search all ready datasets in this project."
        searchable
        filter={({ options }) => options}
        nothingFoundMessage={
          searching || datasets.isPending
            ? "Searching…"
            : "No matching ready datasets"
        }
        // Select also changes its search label on blur and selection; only typing filters the API.
        onInput={(event) => {
          setSearch(event.currentTarget.value);
          setPage(1);
        }}
        data={options.map((dataset) => ({
          value: dataset.id,
          label: dataset.currentVersion
            ? dataset.name
            : `${dataset.name} (not ready)`,
          disabled: !dataset.currentVersion,
        }))}
        value={value}
        disabled={disabled}
        allowDeselect={false}
        onChange={(datasetId) => {
          if (datasetId) {
            setSearch("");
            setPage(1);
            onChange(datasetId);
          }
        }}
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
      {!searching && datasets.data?.totalCount === 0 && (
        <p className="analytics-note">
          {debouncedSearch.trim()
            ? "No ready datasets match this search."
            : "No ready datasets are available in this project yet."}
        </p>
      )}
      {search && (
        <Group gap="xs" mt="xs">
          <span className="analytics-note">Search: {search}</span>
          <Button
            variant="subtle"
            size="compact-xs"
            disabled={disabled}
            onClick={() => {
              setSearch("");
              setPage(1);
            }}
          >
            Clear search
          </Button>
        </Group>
      )}
      {!searching && datasets.data && (
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
