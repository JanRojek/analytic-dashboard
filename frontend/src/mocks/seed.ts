import type {
  Dashboard,
  DataRow,
  Dataset,
  Project,
  TransformStep,
  User,
  Widget,
} from "../data/types.ts";

export interface StoredDataset {
  projectId: string;
  dataset: Dataset;
  columns: string[];
  rows: DataRow[];
}
export interface DemoState {
  version: 1;
  projects: Project[];
  datasets: StoredDataset[];
  dashboards: Dashboard[];
  widgets: Widget[];
  recipes: Record<string, TransformStep[]>;
}
export const demoUser: User = {
  id: "demo-user",
  displayName: "Alex Morgan",
  email: "alex@example.com",
  createdAtUtc: "2026-01-12T09:00:00Z",
};

export function createSeed(): DemoState {
  const retailRows: DataRow[] = Array.from({ length: 240 }, (_, index) => {
    const month = Math.floor(index / 40) + 1;
    return {
      order_date: `2026-${String(month).padStart(2, "0")}-${String((index % 28) + 1).padStart(2, "0")}`,
      month: `2026-${String(month).padStart(2, "0")}`,
      region:
        index % 37 === 0
          ? null
          : ["Europe", "North America", "Asia Pacific", "Other"][index % 4],
      category:
        index % 29 === 0
          ? " Accessories "
          : ["Furniture", "Lighting", "Accessories", "Textiles"][index % 4],
      revenue: String(780 + month * 320 + ((index * 137) % 2890)),
      orders: String(6 + (index % 31)),
    };
  });
  const customerRows: DataRow[] = Array.from({ length: 120 }, (_, index) => ({
    month: `2026-${String(Math.floor(index / 20) + 1).padStart(2, "0")}`,
    channel: ["Email", "Live chat", "Phone"][index % 3],
    satisfaction: index % 23 === 0 ? null : String(3 + (index % 3)),
    response_hours: String(
      Math.round((0.8 + ((index * 7) % 42) / 10) * 10) / 10,
    ),
    tickets: String(8 + (index % 20)),
  }));
  const retail: Dataset = {
    id: "d-retail",
    name: "Retail sales · H1 2026",
    createdAtUtc: "2026-09-16T09:00:00Z",
    currentVersion: {
      id: "v-retail",
      versionNumber: 1,
      originalFileName: "retail_sales_h1_2026.csv",
      rowCount: retailRows.length,
      columnCount: 6,
    },
  };
  const customer: Dataset = {
    id: "d-customer",
    name: "Customer support · H1 2026",
    createdAtUtc: "2026-09-12T09:00:00Z",
    currentVersion: {
      id: "v-customer",
      versionNumber: 1,
      originalFileName: "customer_support_h1_2026.csv",
      rowCount: customerRows.length,
      columnCount: 5,
    },
  };
  const createdAtUtc = "2026-09-17T11:30:00Z";
  return {
    version: 1,
    projects: [
      {
        id: "p-retail",
        name: "Retail performance",
        description:
          "From daily transactions to the bigger picture. Explore revenue, regions, and the products driving growth.",
        color: "teal",
        createdAtUtc: "2026-09-16T09:00:00Z",
      },
      {
        id: "p-customer",
        name: "Customer experience",
        description:
          "Understand satisfaction and make every customer conversation count.",
        color: "blue",
        createdAtUtc: "2026-09-12T09:00:00Z",
      },
      {
        id: "p-operations",
        name: "Operations",
        description: "A fresh workspace for your next question.",
        color: "violet",
        createdAtUtc: "2026-09-10T09:00:00Z",
      },
    ],
    datasets: [
      {
        projectId: "p-retail",
        dataset: retail,
        columns: Object.keys(retailRows[0]),
        rows: retailRows,
      },
      {
        projectId: "p-customer",
        dataset: customer,
        columns: Object.keys(customerRows[0]),
        rows: customerRows,
      },
    ],
    dashboards: [
      {
        id: "b-retail",
        projectId: "p-retail",
        name: "Retail overview",
        createdAtUtc,
      },
    ],
    widgets: [
      {
        id: "w-revenue",
        dashboardId: "b-retail",
        datasetId: retail.id,
        type: "Kpi",
        title: "Total revenue",
        groupByColumn: "month",
        measureColumn: "revenue",
        aggregation: "Sum",
        createdAtUtc,
      },
      {
        id: "w-month",
        dashboardId: "b-retail",
        datasetId: retail.id,
        type: "LineChart",
        title: "Revenue over time",
        groupByColumn: "month",
        measureColumn: "revenue",
        aggregation: "Sum",
        createdAtUtc,
      },
      {
        id: "w-category",
        dashboardId: "b-retail",
        datasetId: retail.id,
        type: "BarChart",
        title: "Revenue by category",
        groupByColumn: "category",
        measureColumn: "revenue",
        aggregation: "Sum",
        createdAtUtc,
      },
      {
        id: "w-region",
        dashboardId: "b-retail",
        datasetId: retail.id,
        type: "PieChart",
        title: "Revenue by region",
        groupByColumn: "region",
        measureColumn: "revenue",
        aggregation: "Sum",
        createdAtUtc,
      },
    ],
    recipes: {},
  };
}
