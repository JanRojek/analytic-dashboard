import { Button } from "@mantine/core";

import type { ColumnProfile, DatasetProfile } from "../../../data/types";

import { Icon } from "../../../components/Icon";

type Props = {
  profile: DatasetProfile;
  selectedField: ColumnProfile | undefined;
  onSelectField: (name: string) => void;
  onBackToIssues: () => void;
};

export function DatasetIssuesPanel({
  profile,
  selectedField,
  onSelectField,
  onBackToIssues,
}: Props) {
  const issues = profile.columns.filter((column) => column.nullCount > 0);

  const issueCount = issues.reduce(
    (total, column) => total + column.nullCount,
    0,
  );

  if (selectedField) {
    return (
      <aside className="dataset-inspector">
        <button className="dataset-inspector-back" onClick={onBackToIssues}>
          <Icon name="back" size={14} />
          Back to issues
        </button>

        <div className="data-section-label">COLUMN</div>

        <div className="dataset-inspector-title">
          <h3>{selectedField.name}</h3>
          <span>{selectedField.type}</span>
        </div>

        <dl className="dataset-inspector-stats">
          <div>
            <dt>Filled values</dt>
            <dd>
              {(profile.rowCount - selectedField.nullCount).toLocaleString()}
            </dd>
          </div>

          <div>
            <dt>Missing values</dt>
            <dd>{selectedField.nullCount.toLocaleString()}</dd>
          </div>

          <div>
            <dt>Minimum</dt>
            <dd>{selectedField.min ?? "—"}</dd>
          </div>

          <div>
            <dt>Maximum</dt>
            <dd>{selectedField.max ?? "—"}</dd>
          </div>

          {selectedField.type === "number" && (
            <div>
              <dt>Average</dt>
              <dd>
                {selectedField.avg === null
                  ? "—"
                  : selectedField.avg.toLocaleString()}
              </dd>
            </div>
          )}
        </dl>

        {selectedField.nullCount > 0 && (
          <div className="dataset-inspector-problem">
            <span>
              <Icon name="warning" size={16} />
            </span>

            <div>
              <strong>
                {selectedField.nullCount.toLocaleString()} missing{" "}
                {selectedField.nullCount === 1 ? "value" : "values"}
              </strong>

              <p>Missing values were detected across the full dataset.</p>

              <Button variant="default" size="xs" disabled>
                Fix missing values
              </Button>
            </div>
          </div>
        )}
      </aside>
    );
  }

  return (
    <aside className="dataset-inspector">
      <div className="dataset-inspector-heading">
        <div>
          <div className="data-section-label">DATA QUALITY</div>
          <h3>
            {issueCount
              ? `${issueCount.toLocaleString()} ${
                  issueCount === 1 ? "issue" : "issues"
                } detected`
              : "No detected issues"}
          </h3>
        </div>
      </div>

      {issues.length ? (
        <div className="dataset-issue-list">
          {issues.map((column) => (
            <button
              key={column.name}
              className="dataset-issue"
              onClick={() => onSelectField(column.name)}
            >
              <span className="dataset-issue-icon">
                <Icon name="warning" size={16} />
              </span>

              <span>
                <strong>{column.name}</strong>
                <small>
                  {column.nullCount.toLocaleString()} missing{" "}
                  {column.nullCount === 1 ? "value" : "values"}
                </small>
              </span>

              <Icon name="chevron" size={14} />
            </button>
          ))}
        </div>
      ) : (
        <div className="dataset-issues-empty">
          <Icon name="check" size={20} />
          <p>No missing values were detected in this dataset.</p>
        </div>
      )}
    </aside>
  );
}
