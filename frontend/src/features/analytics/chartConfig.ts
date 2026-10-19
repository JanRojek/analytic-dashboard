import type {
  Aggregation,
  DatasetProfile,
  QueryConfig,
  WidgetType,
} from "../../data/types";
import type { IconName } from "../../components/Icon";

export const chartTypes: {
  value: WidgetType;
  label: string;
  icon: IconName;
  description: string;
}[] = [
  {
    value: "BarChart",
    label: "Bar",
    icon: "chart",
    description: "Compare categories",
  },
  {
    value: "LineChart",
    label: "Line",
    icon: "line",
    description: "Follow a trend",
  },
  {
    value: "PieChart",
    label: "Donut",
    icon: "pie",
    description: "See the composition",
  },
  {
    value: "Kpi",
    label: "Number",
    icon: "number",
    description: "Focus on a metric",
  },
];

export const aggregations: Aggregation[] = [
  "Sum",
  "Average",
  "Count",
  "Min",
  "Max",
];

export const formatName = (value: string) =>
  value.replace(/_/g, " ").replace(/^./, (char) => char.toUpperCase());

export function defaults(profile: DatasetProfile): QueryConfig {
  const numeric = profile.columns.find((column) => column.type === "number");
  // Prefer a manageable categorical comparison to a high-cardinality date or ID.
  const categories = profile.columns.filter((column) => column.type === "text");
  const group =
    categories.find((column) => {
      const distinct = new Set(
        profile.previewRows.map((row) => row[column.name]).filter(Boolean),
      );
      return distinct.size > 1 && distinct.size <= 12;
    }) ||
    categories[0] ||
    profile.columns.find((column) => column.type === "date") ||
    profile.columns[0];
  return {
    groupByColumn: group?.name || "",
    measureColumn: (numeric || profile.columns[0])?.name || "",
    aggregation: numeric ? "Sum" : "Count",
  };
}

export function queryCaption(config: QueryConfig) {
  return `${config.aggregation === "Count" ? "Count of nonempty" : config.aggregation} ${formatName(config.measureColumn).toLowerCase()} by ${formatName(config.groupByColumn).toLowerCase()}`;
}
