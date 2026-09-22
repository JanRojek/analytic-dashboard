import type { QueryRequest } from './types'

export const queryKeys = {
  me: ['auth', 'me'] as const,
  registrationStatus: ['auth', 'registration-status'] as const,

  projects: ['projects'] as const,
  projectsPage: (page: number, pageSize: number) =>
    ['projects', 'list', { page, pageSize }] as const,
  project: (projectId: string) => ['projects', projectId] as const,

  datasets: (projectId: string) => ['projects', projectId, 'datasets'] as const,
  dataset: (projectId: string, datasetId: string) =>
    ['projects', projectId, 'datasets', datasetId] as const,
  datasetProfile: (projectId: string, datasetId: string) =>
    ['projects', projectId, 'datasets', datasetId, 'profile'] as const,
  datasetQuery: (projectId: string, datasetId: string, query: QueryRequest) =>
    ['projects', projectId, 'datasets', datasetId, 'query', query] as const,

  dashboards: (projectId: string) => ['projects', projectId, 'dashboards'] as const,
  dashboard: (projectId: string, dashboardId: string) =>
    ['projects', projectId, 'dashboards', dashboardId] as const,
  widgets: (projectId: string, dashboardId: string) =>
    ['projects', projectId, 'dashboards', dashboardId, 'widgets'] as const,
  widgetData: (projectId: string, dashboardId: string, widgetId: string) =>
    ['projects', projectId, 'dashboards', dashboardId, 'widgets', widgetId, 'data'] as const,
}
