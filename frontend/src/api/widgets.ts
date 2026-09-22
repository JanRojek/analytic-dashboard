import { request } from './client'
import type { CreateWidgetRequest, Widget, WidgetData } from './types'

const base = (projectId: string, dashboardId: string) =>
  `/projects/${projectId}/dashboards/${dashboardId}/widgets`

export const widgetsApi = {
  list: (projectId: string, dashboardId: string, signal?: AbortSignal) =>
    request<Widget[]>(base(projectId, dashboardId), { signal }),

  create: (projectId: string, dashboardId: string, body: CreateWidgetRequest) =>
    request<{ id: string }>(base(projectId, dashboardId), { method: 'POST', body }),

  remove: (projectId: string, dashboardId: string, widgetId: string) =>
    request<void>(`${base(projectId, dashboardId)}/${widgetId}`, { method: 'DELETE' }),

  data: (projectId: string, dashboardId: string, widgetId: string, signal?: AbortSignal) =>
    request<WidgetData>(`${base(projectId, dashboardId)}/${widgetId}/data`, { signal }),
}
