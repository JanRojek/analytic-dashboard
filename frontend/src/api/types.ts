/**
 * Types mirroring the backend contracts (AnalyticDashboard.Api/Contracts and the
 * Application-layer responses returned directly by endpoints). JSON is camelCase
 * and enums are serialized as strings.
 */

export type ProblemDetails = {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}

// Auth -----------------------------------------------------------------------

export type CurrentUser = {
  id: string
  email: string
  displayName: string
  createdAtUtc: string
}

export type RegisterUserRequest = {
  email: string
  displayName: string
  password: string
}

export type RegisterUserResponse = CurrentUser

export type RegistrationStatus = 'Pending' | 'Confirmed'

export type LoginUserRequest = {
  email: string
  password: string
  rememberMe: boolean
}

// Projects -------------------------------------------------------------------

export type Project = {
  id: string
  name: string
  createdAtUtc: string
}

export type ProjectsPage = {
  items: Project[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

// Datasets -------------------------------------------------------------------

export type DatasetVersionSummary = {
  id: string
  versionNumber: number
  originalFileName: string
  rowCount: number
  columnCount: number
}

/** `currentVersion` is null until the background import has produced a ready version. */
export type Dataset = {
  id: string
  name: string
  createdAtUtc: string
  currentVersion: DatasetVersionSummary | null
}

export type ImportCsvResponse = {
  datasetId: string
  datasetVersionId: string
  importJobId: string
}

export type ColumnType = 'number' | 'text' | 'date'

export type ColumnProfile = {
  name: string
  type: ColumnType
  nullCount: number
  min: string | null
  max: string | null
  avg: number | null
}

export type DatasetProfile = {
  id: string
  name: string
  originalFileName: string
  rowCount: number
  columnCount: number
  columns: ColumnProfile[]
  previewRows: Record<string, string | null>[]
}

// Analytics ------------------------------------------------------------------

export type Aggregation = 'Sum' | 'Average' | 'Min' | 'Max' | 'Count'

export const aggregations: Aggregation[] = ['Sum', 'Average', 'Min', 'Max', 'Count']

export type QueryRequest = {
  groupByColumn: string
  measureColumn: string
  aggregation: Aggregation
}

export type QueryItem = {
  label: string | null
  value: number
}

export type QueryResponse = {
  items: QueryItem[]
}

// Dashboards & widgets -------------------------------------------------------

export type Dashboard = {
  id: string
  projectId: string
  name: string
  createdAtUtc: string
}

export type WidgetType = 'Kpi' | 'BarChart' | 'LineChart' | 'PieChart'

export const widgetTypes: WidgetType[] = ['Kpi', 'BarChart', 'LineChart', 'PieChart']

export type Widget = {
  id: string
  dashboardId: string
  datasetId: string
  type: WidgetType
  title: string
  groupByColumn: string
  measureColumn: string
  aggregation: Aggregation
  createdAtUtc: string
}

export type CreateWidgetRequest = {
  datasetId: string
  type: WidgetType
  title: string
  groupByColumn: string
  measureColumn: string
  aggregation: Aggregation
}

export type WidgetData = {
  widgetId: string
  type: WidgetType
  title: string
  items: QueryItem[]
}
