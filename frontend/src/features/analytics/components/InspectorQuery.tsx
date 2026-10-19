import { ErrorState } from "../../../components/Feedback";
import { Icon } from "../../../components/Icon";

import type { Widget, WidgetInput } from "../../../data/types";

import { useDatasetProfile } from "../../data/hooks";
import { QueryFields } from "./QueryFields";

export function InspectorQuery({
  widget,
  projectId,
  onChange,
  onOpenDataset,
}: {
  widget: Widget;
  projectId: string;
  onChange: (value: Partial<WidgetInput>) => void;
  onOpenDataset: () => void;
}) {
  const profile = useDatasetProfile(projectId, widget.datasetId);

  return (
    <div className="inspector-section">
      <span className="inspector-label">Data & question</span>

      {profile.isPending ? (
        <p className="muted">Reading columns…</p>
      ) : profile.isError ? (
        <ErrorState error={profile.error} retry={() => profile.refetch()} />
      ) : (
        <>
          <button className="inspector-dataset" onClick={onOpenDataset}>
            <Icon name="data" size={15} />
            {profile.data.name}
            <Icon name="chevron" size={13} />
          </button>

          <QueryFields
            profile={profile.data}
            config={widget}
            onChange={onChange}
          />
        </>
      )}

      <p className="analytics-note">
        Changes update the chart preview. Save to keep this configuration in
        this browser.
      </p>
    </div>
  );
}
