import { useState } from "react";
import { Button, SegmentedControl, Select, TextInput } from "@mantine/core";

import { Chart } from "../../../components/Charts";
import {
  EmptyState,
  ErrorState,
  LoadingState,
} from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";

import type {
  Dataset,
  DatasetProfile,
  QueryConfig,
  WidgetInput,
} from "../../../data/types";

import { readExploration, saveExploration } from "../../../mocks/editor";

import { defaults, queryCaption } from "../chartConfig";
import { useExplorationResults } from "../hooks";

import { ChartTypePicker } from "./ChartTypePicker";
import { QueryFields } from "./QueryFields";
import { ResultsTable } from "./ResultsTable";
import { SaveChartDialog } from "./SaveChartDialog";

export function ExplorationWorkspace({
  projectId,
  scope,
  dataset,
  datasets,
  profile,
  onDataset,
}: {
  projectId: string;
  scope: string;
  dataset: Dataset;
  datasets: Dataset[];
  profile: DatasetProfile;
  onDataset: (id: string) => void;
}) {
  const [config, setConfig] = useState<WidgetInput>(() => {
    const draft = readExploration(scope);
    const usable =
      draft?.datasetId === dataset.id &&
      profile.columns.some((column) => column.name === draft.groupByColumn) &&
      profile.columns.some((column) => column.name === draft.measureColumn);
    return usable
      ? draft
      : {
          datasetId: dataset.id,
          ...defaults(profile),
          type: "BarChart",
          title: "",
        };
  });
  const [run, setRun] = useState<QueryConfig>({
    groupByColumn: config.groupByColumn,
    measureColumn: config.measureColumn,
    aggregation: config.aggregation,
  });
  const [view, setView] = useState("chart");
  const [saving, setSaving] = useState(false);
  const [storageError, setStorageError] = useState("");
  const results = useExplorationResults(
    projectId,
    dataset.id,
    dataset.currentVersion?.id,
    run,
    Boolean(run.groupByColumn && run.measureColumn),
  );
  const changed =
    config.groupByColumn !== run.groupByColumn ||
    config.measureColumn !== run.measureColumn ||
    config.aggregation !== run.aggregation;
  const title = config.title.trim() || queryCaption(config);
  const update = (value: WidgetInput) => {
    setConfig(value);
    try {
      saveExploration(scope, value);
      setStorageError("");
    } catch {
      setStorageError(
        "Browser storage is unavailable. Keep this page open to preserve your draft.",
      );
    }
  };
  return (
    <>
      <div className="explore-layout">
        <aside className="explore-controls">
          <div className="analytics-section-label">
            <span className="step-number">1</span> Choose your data
          </div>
          <Select
            aria-label="Dataset"
            data={datasets
              .filter((item) => item.currentVersion)
              .map((item) => ({ value: item.id, label: item.name }))}
            value={dataset.id}
            onChange={(value) => value && onDataset(value)}
            allowDeselect={false}
          />
          <div className="dataset-source-note">
            <Icon name="data" size={14} />
            {profile.rowCount.toLocaleString()} rows · {profile.columnCount}{" "}
            columns
          </div>
          <div className="analytics-section-label">
            <span className="step-number">2</span> Ask a question
          </div>
          <QueryFields
            profile={profile}
            config={config}
            onChange={(value) => update({ ...config, ...value })}
          />
          <Button
            fullWidth
            leftSection={<Icon name="play" size={15} />}
            loading={results.isFetching}
            disabled={!config.measureColumn || !config.groupByColumn}
            onClick={() => {
              const next = {
                groupByColumn: config.groupByColumn,
                measureColumn: config.measureColumn,
                aggregation: config.aggregation,
              };
              if (!changed) void results.refetch();
              else setRun(next);
            }}
          >
            Run query
          </Button>
          <p className="analytics-note">
            Your query uses the current dataset version. No code needed.
          </p>
        </aside>
        <section className="explore-result">
          <div className="explore-result-toolbar">
            <div>
              <span className="eyebrow">VISUALIZATION</span>
              <h2>{title}</h2>
            </div>
            <SegmentedControl
              size="xs"
              value={view}
              onChange={setView}
              data={[
                { value: "chart", label: "Chart" },
                { value: "table", label: "Table" },
              ]}
            />
          </div>
          <div className="explore-chart-area">
            {changed && (
              <div className="query-stale">
                <Icon name="clock" size={15} />
                Your question has changed. Run the query to update these
                results.
              </div>
            )}
            {results.isPending ? (
              <LoadingState label="Running your query…" />
            ) : results.isError ? (
              <ErrorState
                error={results.error}
                retry={() => results.refetch()}
              />
            ) : results.data.length === 0 ? (
              <EmptyState
                icon="search"
                title="No matching values"
                description="Try another measure or aggregation."
              />
            ) : view === "table" ? (
              <ResultsTable items={results.data} config={run} />
            ) : (
              <Chart
                items={results.data}
                type={config.type}
                title={title}
                aggregation={run.aggregation}
              />
            )}
          </div>
          <div className="explore-result-footer">
            <span>
              <i className="status-dot" />
              {results.data?.length || 0} groups{" "}
              <span className="analytics-dot">·</span> {dataset.name}
            </span>
            <span>Source: current version</span>
          </div>
          <div className="explore-style">
            <div>
              <div className="analytics-section-label">
                <span className="step-number">3</span> Make it clear
              </div>
              <ChartTypePicker
                value={config.type}
                onChange={(type) => update({ ...config, type })}
              />
            </div>
            <div className="chart-title-field">
              <TextInput
                label="Chart title"
                placeholder={queryCaption(config)}
                value={config.title}
                maxLength={200}
                onChange={(event) =>
                  update({ ...config, title: event.currentTarget.value })
                }
              />
              <p className="analytics-note">
                A clear title makes the takeaway easier to find.
              </p>
            </div>
          </div>
        </section>
      </div>
      {storageError && (
        <p role="alert" className="analytics-storage-error">
          {storageError}
        </p>
      )}
      <div className="explore-next">
        <div>
          <Icon name="dashboard" size={21} />
          <div>
            <strong>Keep the insight. Build the bigger picture.</strong>
            <p>Add this chart to a dashboard alongside your other findings.</p>
          </div>
        </div>
        <Button
          rightSection={<Icon name="arrow" size={16} />}
          disabled={
            changed ||
            !results.data?.length ||
            results.isFetching ||
            Boolean(results.error)
          }
          onClick={() => setSaving(true)}
        >
          Add to dashboard
        </Button>
      </div>
      <SaveChartDialog
        opened={saving}
        onClose={() => setSaving(false)}
        projectId={projectId}
        input={{ ...config, title }}
      />
    </>
  );
}
