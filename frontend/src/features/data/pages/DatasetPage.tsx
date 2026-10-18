import { useParams } from "react-router-dom";

import { DatasetWorkspace } from "../components/DatasetWorkspace";

export function DatasetPage() {
  const { projectId = "", datasetId = "" } = useParams();

  return (
    <DatasetWorkspace
      key={`${projectId}:${datasetId}`}
      projectId={projectId}
      datasetId={datasetId}
    />
  );
}
