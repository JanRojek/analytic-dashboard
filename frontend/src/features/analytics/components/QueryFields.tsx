import { Select } from "@mantine/core";

import type {
  Aggregation,
  DatasetProfile,
  QueryConfig,
} from "../../../data/types";

import { aggregations, formatName } from "../chartConfig";

export function QueryFields({
  profile,
  config,
  onChange,
}: {
  profile: DatasetProfile;
  config: QueryConfig;
  onChange: (value: QueryConfig) => void;
}) {
  const measures = profile.columns.filter(
    (column) => config.aggregation === "Count" || column.type === "number",
  );

  return (
    <div className="query-fields">
      <Select
        label="Group by"
        description="The categories along your chart"
        data={profile.columns.map((column) => ({
          value: column.name,
          label: formatName(column.name),
        }))}
        value={config.groupByColumn}
        onChange={(value) =>
          value && onChange({ ...config, groupByColumn: value })
        }
        allowDeselect={false}
        searchable
      />

      <div className="query-field-divider" />

      <Select
        label="Measure"
        data={measures.map((column) => ({
          value: column.name,
          label: formatName(column.name),
        }))}
        value={config.measureColumn}
        onChange={(value) =>
          value && onChange({ ...config, measureColumn: value })
        }
        allowDeselect={false}
        searchable
      />

      <Select
        label="Calculate"
        data={aggregations.map((value) => ({
          value,
          label:
            value === "Count"
              ? "Count nonempty values"
              : value === "Min"
                ? "Minimum"
                : value === "Max"
                  ? "Maximum"
                  : value,
        }))}
        value={config.aggregation}
        onChange={(value) => {
          if (!value) return;

          const aggregation = value as Aggregation;
          const measure = profile.columns.find(
            (column) => column.name === config.measureColumn,
          );

          onChange({
            ...config,
            aggregation,
            measureColumn:
              aggregation !== "Count" && measure?.type !== "number"
                ? profile.columns.find((column) => column.type === "number")
                    ?.name || ""
                : config.measureColumn,
          });
        }}
        allowDeselect={false}
      />

      {!measures.length && (
        <p className="analytics-note">
          This dataset has no numeric columns. Use Count to count nonempty
          values.
        </p>
      )}
    </div>
  );
}
