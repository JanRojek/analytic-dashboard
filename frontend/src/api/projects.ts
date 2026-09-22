import { request } from './client'
import type { Project, ProjectsPage } from './types'

export const projectsApi = {
  list: (page = 1, pageSize = 24, signal?: AbortSignal) =>
    request<ProjectsPage>(`/projects?page=${page}&pageSize=${pageSize}`, { signal }),

  get: (projectId: string, signal?: AbortSignal) =>
    request<Project>(`/projects/${projectId}`, { signal }),

  create: (name: string) => request<Project>('/projects', { method: 'POST', body: { name } }),

  rename: (projectId: string, name: string) =>
    request<Project>(`/projects/${projectId}`, { method: 'PATCH', body: { name } }),

  remove: (projectId: string) => request<void>(`/projects/${projectId}`, { method: 'DELETE' }),
}
