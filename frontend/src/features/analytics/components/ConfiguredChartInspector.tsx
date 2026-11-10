import { startTransition, useEffect, useRef, useState } from "react";
import { Button, TextInput } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type { Widget, WidgetInput } from "../../../data/types";
import { ChartTypePicker } from "./ChartTypePicker";
import { InspectorQuery } from "./InspectorQuery";

type Props = {
  widget: Widget;
  projectId: string;
  saveCompleted: boolean;
  onChange: (patch: Partial<WidgetInput>, groupId?: string) => void;
  onOpenDataset: () => void;
  onInspectResults: () => void;
  onRemove: () => void;
};

export function ConfiguredChartInspector({
  widget,
  projectId,
  saveCompleted,
  onChange,
  onOpenDataset,
  onInspectResults,
  onRemove,
}: Props) {
  const [title, setTitle] = useState(widget.title);
  const titleFocused = useRef(false);
  const titleGroup = useRef<string | null>(null);

  useEffect(() => {
    if (saveCompleted) {
      titleGroup.current = null;
    }
  }, [saveCompleted]);

  useEffect(() => {
    if (!titleFocused.current) {
      setTitle(widget.title);
    }
  }, [widget.id, widget.title]);

  function changeTitle(nextTitle: string) {
    setTitle(nextTitle);

    const groupId = titleGroup.current ?? crypto.randomUUID();
    titleGroup.current = groupId;

    startTransition(() => {
      onChange({ title: nextTitle }, groupId);
    });
  }

  function finishTitleEditing() {
    titleFocused.current = false;
    titleGroup.current = null;
  }

  return (
      <>
        <div className="inspector-section">
          <TextInput
              label="Title"
              value={title}
              maxLength={200}
              onFocus={() => {
                titleFocused.current = true;
                titleGroup.current = crypto.randomUUID();
              }}
              onBlur={finishTitleEditing}
              onChange={(event) => changeTitle(event.currentTarget.value)}
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
