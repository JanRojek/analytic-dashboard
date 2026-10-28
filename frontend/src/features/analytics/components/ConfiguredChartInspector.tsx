import { Button, TextInput } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type { Widget, WidgetInput } from "../../../data/types";
import { ChartTypePicker } from "./ChartTypePicker";
import { InspectorQuery } from "./InspectorQuery";

type Props = {
    widget: Widget;
    projectId: string;
    onChange: (patch: Partial<WidgetInput>) => void;
    onOpenDataset: () => void;
    onInspectResults: () => void;
    onRemove: () => void;
};

export function ConfiguredChartInspector({
    widget,
    projectId,
    onChange,
    onOpenDataset,
    onInspectResults,
    onRemove,
}: Props) {
    return (
        <>
            <div className="inspector-section">
                <TextInput
                    label="Title"
                    value={widget.title}
                    maxLength={200}
                    onChange={(event) =>
                        onChange({ title: event.currentTarget.value })
                    }
                />

                <details className="inspector-visualization-details">
                    <summary>
                        Change visualization
                        <Icon name="chevron" size={14} />
                    </summary>

                    <div className="inspector-visualization-picker">
                        <ChartTypePicker
                            value={widget.type}
                            onChange={(type) => onChange({ type })}
                        />
                    </div>
                </details>
            </div>

            <InspectorQuery
                key={widget.id}
                widget={widget}
                projectId={projectId}
                onChange={onChange}
                onOpenDataset={onOpenDataset}
            />

            <div className="inspector-section">
                <Button
                    fullWidth
                    variant="default"
                    size="xs"
                    leftSection={<Icon name="data" size={15} />}
                    onClick={onInspectResults}
                >
                    Inspect results
                </Button>

                <Button
                    fullWidth
                    variant="subtle"
                    color="red"
                    size="xs"
                    mt={8}
                    leftSection={<Icon name="trash" size={15} />}
                    onClick={onRemove}
                >
                    Remove chart
                </Button>
            </div>
        </>
    );
}
