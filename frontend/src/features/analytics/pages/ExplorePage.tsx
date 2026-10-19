import { Button } from "@mantine/core";
import { Link, useParams, useSearchParams } from "react-router-dom";

import {
  EmptyState,
  ErrorState,
  LoadingState,
  PageHeading,
} from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";
import { useDatasets, useDatasetProfile } from "../../data/hooks";
import { useSession } from "../../../session/SessionContext";
import { editorScope, readExploration } from "../../../mocks/editor";

import { ExplorationWorkspace } from "../components/ExplorationWorkspace";

export function ExplorePage() {
  const { projectId = "" } = useParams();
  const { mode, session } = useSession();
  const [params, setParams] = useSearchParams();

  const scope = editorScope(mode, session?.user.id || "", projectId);
  const draft = readExploration(scope);
  const datasets = useDatasets(projectId);

  const selected = params.get("dataset") || draft?.datasetId;
  const dataset =
    datasets.data?.find((item) => item.id === selected) ||
    datasets.data?.find((item) => item.currentVersion);

  const profile = useDatasetProfile(
    projectId,
    dataset?.id ?? "",
    Boolean(dataset),
  );

  return (
    <div className="page-content analytics-page">
      <PageHeading
        eyebrow="FROM DATA TO INSIGHT"
        title="Ask your data a question."
        description="Start with a comparison. Shape it into a chart. Give it a place on your dashboard."
      />

      {datasets.isPending ? (
        <LoadingState />
      ) : datasets.isError ? (
        <ErrorState error={datasets.error} retry={() => datasets.refetch()} />
      ) : !dataset ? (
        <EmptyState
          icon="chart"
          title="Every insight starts with data"
          description="Add a dataset to your project, then explore its patterns here."
          action={
            <Button component={Link} to={`/projects/${projectId}/data`}>
              Go to data <Icon name="arrow" />
            </Button>
          }
        />
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
          datasets={datasets.data || []}
          profile={profile.data}
          onDataset={(id) => setParams({ dataset: id })}
        />
      )}
    </div>
  );
}
