import { useState } from "react";
import { ActionIcon, Button, Select, TextInput } from "@mantine/core";

import type {
    DatasetProfile,
    TransformStep,
} from "../../../data/types";

import { Icon } from "../../../components/Icon";

type Props = {
    profile: DatasetProfile;
    steps: TransformStep[];
    editable: boolean;
    onStepsChange: (steps: TransformStep[]) => void;
};

const transformations = [
    { value: "trim", label: "Trim whitespace" },
    { value: "fill", label: "Fill empty values" },
    { value: "drop-empty", label: "Remove rows with empty values" },
    { value: "deduplicate", label: "Remove duplicate rows" },
];

function stepName(step: TransformStep) {
    switch (step.kind) {
        case "trim":
            return "Trim whitespace";
        case "fill":
            return "Fill empty values";
        case "drop-empty":
            return "Remove rows with empty values";
        case "deduplicate":
            return "Remove duplicate rows";
    }
}

function stepDetail(step: TransformStep) {
    if (step.kind === "deduplicate") {
        return "All fields";
    }

    if (step.kind === "fill") {
        return `${step.column} → ${step.value}`;
    }

    return step.column;
}

export function AppliedStepsPanel({
    profile,
    steps,
    editable,
    onStepsChange,
}: Props) {
    const [adding, setAdding] = useState(false);

    const [kind, setKind] =
        useState<TransformStep["kind"]>("trim");

    const [column, setColumn] = useState(
        profile.columns[0]?.name ?? "",
    );

    const [value, setValue] = useState("");

    function addStep() {
        if (kind !== "deduplicate" && !column) {
            return;
        }

        if (kind === "fill" && !value.trim()) {
            return;
        }

        const step: TransformStep = {
            id: crypto.randomUUID(),
            kind,
            column: kind === "deduplicate" ? "" : column,
            ...(kind === "fill" ? { value: value.trim() } : {}),
        };

        onStepsChange([...steps, step]);

        setValue("");
        setAdding(false);
    }

    return (
        <section className="applied-steps-panel">
            <div className="applied-steps-heading">
                <div className="data-section-label">APPLIED STEPS</div>

                {editable && (
                    <ActionIcon
                        variant="subtle"
                        color="gray"
                        size="sm"
                        aria-label="Add preparation step"
                        onClick={() => setAdding((current) => !current)}
                    >
                        <Icon name={adding ? "close" : "plus"} size={14} />
                    </ActionIcon>
                )}
            </div>

            <div className="applied-step">
        <span className="applied-step-icon">
          <Icon name="data" size={14} />
        </span>

                <div>
                    <strong>Source</strong>
                    <small>Original imported dataset</small>
                </div>
            </div>

            {steps.map((step) => (
                <div className="applied-step" key={step.id}>
          <span className="applied-step-icon">
            <Icon name="settings" size={14} />
          </span>

                    <div>
                        <strong>{stepName(step)}</strong>
                        <small>{stepDetail(step)}</small>
                    </div>

                    {editable && (
                        <ActionIcon
                            variant="subtle"
                            color="gray"
                            size="sm"
                            aria-label={`Remove ${stepName(step)}`}
                            onClick={() =>
                                onStepsChange(
                                    steps.filter((item) => item.id !== step.id),
                                )
                            }
                        >
                            <Icon name="close" size={13} />
                        </ActionIcon>
                    )}
                </div>
            ))}

            {adding && (
                <div className="applied-step-form">
                    <Select
                        label="Operation"
                        value={kind}
                        data={transformations}
                        allowDeselect={false}
                        onChange={(next) =>
                            setKind(next as TransformStep["kind"])
                        }
                    />

                    {kind !== "deduplicate" && (
                        <Select
                            label="Column"
                            data={profile.columns.map((item) => item.name)}
                            value={column}
                            searchable
                            allowDeselect={false}
                            onChange={(next) => setColumn(next ?? "")}
                        />
                    )}

                    {kind === "fill" && (
                        <TextInput
                            label="Replace empty values with"
                            value={value}
                            onChange={(event) =>
                                setValue(event.currentTarget.value)
                            }
                        />
                    )}

                    <div className="applied-step-form-actions">
                        <Button
                            variant="default"
                            size="xs"
                            onClick={() => setAdding(false)}
                        >
                            Cancel
                        </Button>

                        <Button
                            size="xs"
                            disabled={
                                (kind !== "deduplicate" && !column) ||
                                (kind === "fill" && !value.trim())
                            }
                            onClick={addStep}
                        >
                            Add step
                        </Button>
                    </div>
                </div>
            )}
        </section>
    );
}
