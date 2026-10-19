import { Tooltip } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type { WidgetType } from "../../../data/types";
import { chartTypes } from "../chartConfig";

export function ChartTypePicker({
  value,
  onChange,
}: {
  value: WidgetType;
  onChange: (value: WidgetType) => void;
}) {
  return (
    <div className="chart-type-picker" role="group" aria-label="Chart type">
      {chartTypes.map((type) => (
        <Tooltip key={type.value} label={type.description}>
          <button
            type="button"
            aria-pressed={value === type.value}
            onClick={() => onChange(type.value)}
          >
            <Icon name={type.icon} size={20} />
            <span>{type.label}</span>
          </button>
        </Tooltip>
      ))}
    </div>
  );
}
