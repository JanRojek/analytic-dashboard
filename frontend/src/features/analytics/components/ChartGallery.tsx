import { Tooltip } from "@mantine/core";

import { Icon } from "../../../components/Icon";
import type { WidgetType } from "../../../data/types";

import { chartTypes } from "../chartConfig";

interface ChartGalleryProps {
  onAdd: (type: WidgetType) => void;
}

export function ChartGallery({ onAdd }: ChartGalleryProps) {
  return (
    <section className="chart-gallery" aria-label="Chart library">
      <span className="inspector-label">Visualizations</span>

      <div className="chart-gallery-grid">
        {chartTypes.map((chart) => (
          <Tooltip
            key={chart.value}
            label={chart.description}
            position="top"
            withArrow
          >
            <button
              type="button"
              className="chart-gallery-item"
              aria-label={`Add ${chart.label} visualization`}
              onClick={() => onAdd(chart.value)}
            >
              <Icon name={chart.icon} size={22} />
              <span>{chart.label}</span>
            </button>
          </Tooltip>
        ))}
      </div>
    </section>
  );
}
