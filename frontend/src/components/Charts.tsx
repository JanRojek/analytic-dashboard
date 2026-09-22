import { useEffect, useId, useRef, useState } from "react";
import type { Aggregation, QueryItem, WidgetType } from "../data/types";
import "../styles/analytics.css";

const palette = [
  "#28634f",
  "#8ead96",
  "#7895aa",
  "#bf9258",
  "#a39bbf",
  "#5c8580",
  "#c0bdac",
];
const number = (value: number) =>
  new Intl.NumberFormat("en", {
    maximumFractionDigits: 2,
    notation: Math.abs(value) >= 10000 ? "compact" : "standard",
  }).format(value);
const fullNumber = (value: number) =>
  new Intl.NumberFormat("en", { maximumFractionDigits: 2 }).format(value);
const label = (item: QueryItem) => item.label ?? "(empty)";

export function Chart({
  type,
  items,
  title = "Query results",
  compact = false,
  aggregation = "Sum",
}: {
  type: WidgetType;
  items: QueryItem[];
  title?: string;
  compact?: boolean;
  aggregation?: Aggregation;
}) {
  const id = useId();
  const plot = useRef<HTMLDivElement>(null);
  const [plotWidth, setPlotWidth] = useState(640);
  const valid = items.filter((item) => Number.isFinite(item.value));
  const hasValues = valid.length > 0;
  useEffect(() => {
    if (!plot.current) return;
    const observer = new ResizeObserver((entries) => {
      const width = Math.round(entries[0].contentRect.width);
      if (width > 0) setPlotWidth(width);
    });
    observer.observe(plot.current);
    return () => observer.disconnect();
  }, [type, hasValues]);
  if (!valid.length)
    return (
      <div className="chart-no-data">
        No values to display. Try a different measure.
      </div>
    );
  if (type === "Kpi") {
    // Grouped averages cannot be rolled up without each group's row count.
    // Show the actual group averages instead of inventing an overall average.
    if (aggregation === "Average") {
      const groups = compact ? valid.slice(0, 2) : valid;
      return (
        <div
          className={`chart-group-kpis ${compact ? "compact" : ""}`}
          aria-label={`${title}: averages by group`}
        >
          <span className="group-kpi-label">Average by group</span>
          <dl>
            {groups.map((item, index) => (
              <div key={index}>
                <dt>{label(item)}</dt>
                <dd title={fullNumber(item.value)}>{number(item.value)}</dd>
              </div>
            ))}
          </dl>
          {compact && valid.length > 2 && (
            <span className="group-kpi-more">
              + {valid.length - 2} more groups
            </span>
          )}
        </div>
      );
    }
    const sum = valid.reduce((total, item) => total + item.value, 0);
    const highest = valid.reduce((best, item) =>
      item.value > best.value ? item : best,
    );
    const lowest = valid.reduce((best, item) =>
      item.value < best.value ? item : best,
    );
    const value =
      aggregation === "Min"
        ? lowest.value
        : aggregation === "Max"
          ? highest.value
          : sum;
    const detail =
      aggregation === "Min"
        ? `Lowest value · ${label(lowest)}`
        : aggregation === "Max"
          ? `Highest value · ${label(highest)}`
          : `Across ${valid.length} ${valid.length === 1 ? "group" : "groups"}`;
    return (
      <div
        className={`chart-kpi ${compact ? "compact" : ""}`}
        aria-label={`${title}: ${fullNumber(value)}. ${detail}`}
      >
        <strong title={fullNumber(value)}>{number(value)}</strong>
        <span>
          <i />
          {detail}
        </span>
        {!compact && (
          <div className="kpi-composition" aria-hidden="true">
            {valid.slice(0, 12).map((item, index) => (
              <b
                key={index}
                style={{
                  background: palette[index % palette.length],
                  flex: Math.max(Math.abs(item.value), 1),
                }}
              />
            ))}
          </div>
        )}
      </div>
    );
  }
  if (type === "PieChart") {
    if (valid.some((item) => item.value < 0))
      return (
        <div className="chart-no-data">
          A donut cannot represent negative values. Choose a bar or line chart.
        </div>
      );
    const total = valid.reduce((sum, item) => sum + item.value, 0);
    if (!total)
      return (
        <div className="chart-no-data">
          All values are zero. Choose a bar chart to compare groups.
        </div>
      );
    const sorted = [...valid].sort((a, b) => b.value - a.value);
    const slices =
      sorted.length > 7
        ? [
            ...sorted.slice(0, 6),
            {
              label: "Other groups",
              value: sorted.slice(6).reduce((sum, item) => sum + item.value, 0),
            },
          ]
        : sorted;
    return (
      <div className={`chart-donut ${compact ? "compact" : ""}`}>
        <svg viewBox="0 0 240 240" role="img" aria-labelledby={id}>
          <title id={id}>
            {title}.{" "}
            {slices
              .map((item) => `${label(item)}: ${fullNumber(item.value)}`)
              .join("; ")}
          </title>
          <circle
            cx="120"
            cy="120"
            r="78"
            stroke="#edf0ea"
            fill="none"
            strokeWidth="30"
          />
          {slices.map((item, index) => {
            const offset =
              (slices
                .slice(0, index)
                .reduce((sum, slice) => sum + slice.value, 0) /
                total) *
              100;
            return (
              <circle
                key={index}
                cx="120"
                cy="120"
                r="78"
                fill="none"
                stroke={palette[index]}
                strokeWidth="30"
                pathLength="100"
                strokeDasharray={`${(item.value / total) * 100} ${100 - (item.value / total) * 100}`}
                strokeDashoffset={-offset}
                transform="rotate(-90 120 120)"
              >
                <title>
                  {label(item)}: {fullNumber(item.value)} (
                  {((item.value / total) * 100).toFixed(1)}%)
                </title>
              </circle>
            );
          })}
          <text x="120" y="116" textAnchor="middle" className="donut-total">
            {number(total)}
          </text>
          <text x="120" y="138" textAnchor="middle" className="donut-caption">
            combined values
          </text>
        </svg>
        {!compact && (
          <ul className="chart-legend">
            {slices.map((item, index) => (
              <li key={index}>
                <i style={{ background: palette[index] }} />
                <span title={label(item)}>{label(item)}</span>
                <strong>{((item.value / total) * 100).toFixed(1)}%</strong>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }
  const shown = valid.slice(0, 24);
  const width = Math.max(compact ? 120 : 260, plotWidth);
  const height = compact ? 115 : Math.max(225, Math.min(290, width * 0.6));
  const left = compact ? 6 : 46,
    right = 14,
    top = 20,
    bottom = compact ? 5 : 40;
  const min = Math.min(0, ...shown.map((item) => item.value));
  const upper = Math.max(0, ...shown.map((item) => item.value));
  const max = upper === min ? upper + 1 : upper;
  const span = max - min || 1;
  const y = (value: number) =>
    top + ((max - value) / span) * (height - top - bottom);
  const step = (width - left - right) / shown.length;
  const x = (index: number) => left + step * (index + 0.5);
  const points = shown
    .map((item, index) => `${x(index)},${y(item.value)}`)
    .join(" ");
  return (
    <div ref={plot} className={`chart-cartesian ${compact ? "compact" : ""}`}>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby={id}>
        <title id={id}>
          {title}.{" "}
          {shown
            .map((item) => `${label(item)}: ${fullNumber(item.value)}`)
            .join("; ")}
        </title>
        {!compact &&
          [0, 1, 2, 3, 4].map((tick) => {
            const value = min + (span * tick) / 4;
            return (
              <g key={tick}>
                <line
                  x1={left}
                  x2={width - right}
                  y1={y(value)}
                  y2={y(value)}
                  stroke="#e9ede7"
                  strokeDasharray={tick === 0 ? undefined : "3 4"}
                />
                <text
                  x={left - 12}
                  y={y(value) + 4}
                  textAnchor="end"
                  className="chart-tick"
                >
                  {number(value)}
                </text>
              </g>
            );
          })}
        {min < 0 && (
          <line
            x1={left}
            x2={width - right}
            y1={y(0)}
            y2={y(0)}
            stroke="#a9b5ac"
          />
        )}
        {type === "LineChart" ? (
          <>
            <polygon
              points={`${x(0)},${y(0)} ${points} ${x(shown.length - 1)},${y(0)}`}
              fill="#28634f"
              opacity=".055"
            />
            <polyline
              points={points}
              fill="none"
              stroke="#28634f"
              strokeWidth="2.8"
              strokeLinejoin="round"
              strokeLinecap="round"
            />
            {shown.map((item, index) => (
              <circle
                key={index}
                cx={x(index)}
                cy={y(item.value)}
                r={compact ? 0 : 4}
                fill="#fff"
                stroke="#28634f"
                strokeWidth="2"
              >
                <title>
                  {label(item)}: {fullNumber(item.value)}
                </title>
              </circle>
            ))}
          </>
        ) : (
          shown.map((item, index) => (
            <rect
              key={index}
              x={x(index) - Math.min(step * 0.6, 60) / 2}
              y={Math.min(y(item.value), y(0))}
              width={Math.min(step * 0.6, 60)}
              height={Math.max(Math.abs(y(item.value) - y(0)), 1)}
              fill={index === 0 ? "#28634f" : "#91af9b"}
              rx="3"
            >
              <title>
                {label(item)}: {fullNumber(item.value)}
              </title>
            </rect>
          ))
        )}
        {!compact &&
          shown.map(
            (item, index) =>
              (shown.length <= Math.floor(width / 75) ||
                index %
                  Math.ceil(
                    shown.length / Math.max(1, Math.floor(width / 75)),
                  ) ===
                  0) && (
                <text
                  key={index}
                  x={x(index)}
                  y={height - 15}
                  textAnchor="middle"
                  className="chart-tick"
                >
                  {label(item).length > 13
                    ? `${label(item).slice(0, 11)}…`
                    : label(item)}
                </text>
              ),
          )}
      </svg>
      {!compact && valid.length > 24 && (
        <p className="chart-footnote">
          Showing the first 24 of {valid.length} groups. Open the results table
          to see all values.
        </p>
      )}
    </div>
  );
}
