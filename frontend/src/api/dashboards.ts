import { request } from './client'
import type { Dashboard } from './types'

const base = (projectId: string) => `/projects/${projectId}/dashboards`

export const dashboardsApi = {
  list: (projectId: string, signal?: AbortSignal) =>
    request<Dashboard[]>(base(projectId), { signal }),

  get: (projectId: string, dashboardId: string, signal?: AbortSignal) =>
    request<Dashboard>(`${base(projectId)}/${dashboardId}`, { signal }),

  create: (projectId: string, name: string) =>
    request<{ id: string }>(base(projectId), { method: 'POST', body: { name } }),

  remove: (projectId: string, dashboardId: string) =>
    request<void>(`${base(projectId)}/${dashboardId}`, { method: 'DELETE' }),
}
