export type DataMode = "demo" | "api";
export type Aggregation = "Sum" | "Average" | "Min" | "Max" | "Count";
export type WidgetType = "Kpi" | "BarChart" | "LineChart" | "PieChart";

export interface User {
  id: string;
  email: string;
  displayName: string;
  createdAtUtc: string;
}
export interface Project {
  id: string;
  name: string;
  createdAtUtc: string;
  description?: string;
  color?: string;
}
export interface DatasetVersion {
  id: string;
  versionNumber: number;
  originalFileName: string;
  rowCount: number;
  columnCount: number;
}

export type ImportJobStatus = "Pending" | "Running" | "Completed" | "Failed";

export interface ImportStatus {
  importJobId: string;
  datasetVersionId: string;
  status: ImportJobStatus;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  errorMessage: string | null;
}

export interface PaginationParams {
  page?: number;
  pageSize?: number;
}

export interface DatasetListParams extends PaginationParams {
  search?: string;
  readyOnly?: boolean;
}

export interface DatasetSummary {
  totalCount: number;
  readyCount: number;
  totalRows: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Dataset {
  id: string;
  name: string;
  createdAtUtc: string;
  currentVersion: DatasetVersion | null;
}
export interface ColumnProfile {
  name: string;
  type: "number" | "date" | "text";
  nullCount: number;
  min: string | null;
  max: string | null;
  avg: number | null;
}
export type DataRow = Record<string, string | null>;

export interface DatasetRowsPage {
  items: DataRow[];
  page: number;
  pageSize: number;
  totalRows: number;
  totalPages: number;
}

export interface DatasetProfile {
  id: string;
  name: string;
  originalFileName: string;
  rowCount: number;
  columnCount: number;
  columns: ColumnProfile[];
  previewRows: DataRow[];
}
export interface DashboardListItem {
  id: string;
  projectId: string;
  name: string;
  createdAtUtc: string;
}

export interface Dashboard extends DashboardListItem {
  revision: number;
}

export type EditorDashboard = Pick<
  Dashboard,
  "id" | "projectId" | "name" | "revision"
>;

export interface DashboardEditorState {
  dashboard: EditorDashboard;
  widgets: Widget[];
}

export interface UpdateDashboardWidgetsResponse {
  revision: number;
}
export interface QueryConfig {
  groupByColumn: string;
  measureColumn: string;
  aggregation: Aggregation;
}
export interface QueryItem {
  label: string | null;
  value: number;
}
export interface WidgetInput extends QueryConfig {
  datasetId: string;
  type: WidgetType;
  title: string;
}

export interface Widget extends Omit<WidgetInput, "datasetId"> {
  id: string;
  dashboardId: string;
  datasetId: string | null;
  createdAtUtc: string;
  layout: WidgetLayout;
}

export interface WidgetLayout {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CreateWidgetInput extends WidgetInput {
  layout: WidgetLayout;
}

export interface DashboardWidgetUpdate extends Omit<WidgetInput, "datasetId"> {
  widgetId: string;
  datasetId: string | null;
  layout: WidgetLayout;
}

export interface TransformStep {
  id: string;
  kind: "trim" | "fill" | "drop-empty" | "deduplicate";
  column: string;
  value?: string;
}

export interface DataAdapter {
  listProjects(params?: PaginationParams): Promise<PagedResult<Project>>;
  getProject(projectId: string): Promise<Project>;
  createProject(name: string, description?: string): Promise<Project>;
  renameProject(projectId: string, name: string): Promise<Project>;
  deleteProject(projectId: string): Promise<void>;
  listDatasets(
    projectId: string,
    params?: DatasetListParams,
  ): Promise<PagedResult<Dataset>>;
  getDatasetsSummary(projectId: string): Promise<DatasetSummary>;
  getDataset(projectId: string, datasetId: string): Promise<Dataset>;
  getImportStatus(
    projectId: string,
    datasetId: string,
  ): Promise<ImportStatus | null>;
  getProfile(projectId: string, datasetId: string): Promise<DatasetProfile>;
  getDatasetRows(
    projectId: string,
    datasetId: string,
    page: number,
    pageSize: number,
  ): Promise<DatasetRowsPage>;
  importCsv(projectId: string, file: File): Promise<{ datasetId: string }>;
  deleteDataset(projectId: string, datasetId: string): Promise<void>;
  query(
    projectId: string,
    datasetId: string,
    config: QueryConfig,
  ): Promise<QueryItem[]>;
  listDashboards(
    projectId: string,
    params?: PaginationParams,
  ): Promise<PagedResult<DashboardListItem>>;
  getDashboard(projectId: string, dashboardId: string): Promise<Dashboard>;
  getDashboardEditorState(
    projectId: string,
    dashboardId: string,
  ): Promise<DashboardEditorState>;
  createDashboard(projectId: string, name: string): Promise<Dashboard>;
  deleteDashboard(projectId: string, dashboardId: string): Promise<void>;
  listWidgets(projectId: string, dashboardId: string): Promise<Widget[]>;
  createWidget(
    projectId: string,
    dashboardId: string,
    input: CreateWidgetInput,
  ): Promise<Widget>;
  deleteWidget(
    projectId: string,
    dashboardId: string,
    widgetId: string,
  ): Promise<void>;
  widgetData(
    projectId: string,
    dashboardId: string,
    widgetId: string,
  ): Promise<QueryItem[]>;
  updateDashboardWidgets(
    projectId: string,
    dashboardId: string,
    updates: DashboardWidgetUpdate[],
    expectedRevision: number,
  ): Promise<UpdateDashboardWidgetsResponse>;
}
