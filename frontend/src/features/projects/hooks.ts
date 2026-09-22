import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { projectsApi } from '../../api/projects'
import { datasetsApi } from '../../api/datasets'
import { dashboardsApi } from '../../api/dashboards'
import { queryKeys } from '../../api/queryKeys'
import type { Project } from '../../api/types'

export function useProjectsPage(page: number, pageSize = 24, enabled = true) {
  return useQuery({
    queryKey: queryKeys.projectsPage(page, pageSize),
    queryFn: ({ signal }) => projectsApi.list(page, pageSize, signal),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useProject(projectId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.project(projectId ?? ''),
    queryFn: ({ signal }) => projectsApi.get(projectId!, signal),
    enabled: Boolean(projectId),
    staleTime: 60_000,
  })
}

/** Dataset and dashboard counts for a project card; shares cache with the workspace. */
export function useProjectCounts(projectId: string, enabled = true) {
  const datasets = useQuery({
    queryKey: queryKeys.datasets(projectId),
    queryFn: ({ signal }) => datasetsApi.list(projectId, signal),
    enabled,
    staleTime: 60_000,
  })
  const dashboards = useQuery({
    queryKey: queryKeys.dashboards(projectId),
    queryFn: ({ signal }) => dashboardsApi.list(projectId, signal),
    enabled,
    staleTime: 60_000,
  })
  return {
    datasets: datasets.data?.length,
    readyDatasets: datasets.data?.filter((d) => d.currentVersion !== null).length,
    dashboards: dashboards.data?.length,
    isPending: datasets.isPending || dashboards.isPending,
  }
}

export function useCreateProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => projectsApi.create(name),
    onSuccess: (project) => {
      queryClient.setQueryData(queryKeys.project(project.id), project)
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects })
    },
  })
}

export function useRenameProject(projectId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => projectsApi.rename(projectId, name),
    onSuccess: (project: Project) => {
      queryClient.setQueryData(queryKeys.project(project.id), project)
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects })
    },
  })
}

export function useDeleteProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (projectId: string) => projectsApi.remove(projectId),
    onSuccess: (_, projectId) => {
      queryClient.removeQueries({ queryKey: queryKeys.project(projectId) })
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects })
    },
  })
}
