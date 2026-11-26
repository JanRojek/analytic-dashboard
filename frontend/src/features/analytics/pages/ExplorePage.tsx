import { Button } from "@mantine/core";
import { Link, useParams, useSearchParams } from "react-router-dom";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import { useDataset, useDatasets, useDatasetProfile } from "../../data/hooks";
import { useSession } from "../../../session/SessionContext";
import { editorScope, readExploration } from "../../../mocks/editor";

import { DatasetSelect } from "../../data/components/DatasetSelect";

import { ExplorationWorkspace } from "../components/ExplorationWorkspace";

export function ExplorePage() {
  const { projectId = "" } = useParams();
  const { mode, session } = useSession();
  const [params, setParams] = useSearchParams();

  const scope = editorScope(mode, session?.user.id || "", projectId);
  const draft = readExploration(scope);
  const selected = params.get("dataset") || draft?.datasetId;
  const datasets = useDatasets(projectId, {}, !selected);
  const datasetId =
    selected ||
    datasets.data?.items.find((item) => item.currentVersion)?.id ||
    "";
  const detail = useDataset(projectId, datasetId, Boolean(datasetId));
  const dataset = detail.data;
  const datasetSelector = (
    <DatasetSelect
      projectId={projectId}
      value={datasetId || null}
      onChange={(id) => setParams({ dataset: id })}
    />
  );

  const profile = useDatasetProfile(
    projectId,
    dataset?.id ?? "",
    Boolean(dataset?.currentVersion),
  );

  return (
    <div className="page-content analytics-page">
      <PageHeading
        eyebrow="FROM DATA TO INSIGHT"
        title="Ask your data a question."
        description="Start with a comparison. Shape it into a chart. Give it a place on your dashboard."
      />

      {(!selected && datasets.isPending) ||
      (Boolean(datasetId) && detail.isPending) ? (
        <LoadingState />
      ) : detail.isError ? (
        <>
          {datasetSelector}
          <ErrorState
            error={detail.error}
            retry={() => void detail.refetch()}
          />
        </>
      ) : !selected && datasets.isError ? (
        <ErrorState error={datasets.error} retry={() => datasets.refetch()} />
      ) : !dataset?.currentVersion ? (
        <>
          {datasetSelector}
          <EmptyState
            icon="chart"
            title="Choose a ready dataset"
            description="Browse the dataset pages above, or import a new source. Datasets still being imported cannot be explored yet."
            action={
              <Button component={Link} to={`/projects/${projectId}/data`}>
                Go to data <Icon name="arrow" />
              </Button>
            }
          />
        </>
      ) : profile.isPending ? (
        <LoadingState label="Reading the dataset columns…" />
      ) : profile.isError ? (
        <ErrorState error={profile.error} retry={() => profile.refetch()} />
      ) : (
        <ExplorationWorkspace
          key={`${scope}:${dataset.id}:${dataset.currentVersion?.id}`}
          projectId={projectId}
          scope={scope}
          dataset={dataset}
          datasetSelector={datasetSelector}
          profile={profile.data}
        />
      )}
    </div>
  );
}
