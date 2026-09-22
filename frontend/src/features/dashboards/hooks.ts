import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { dashboardsApi } from '../../api/dashboards'
import { widgetsApi } from '../../api/widgets'
import { queryKeys } from '../../api/queryKeys'
import type { CreateWidgetRequest, Dashboard, Widget } from '../../api/types'

export function useDashboards(projectId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.dashboards(projectId ?? ''),
    queryFn: ({ signal }) => dashboardsApi.list(projectId!, signal),
    enabled: Boolean(projectId),
  })
}

export function useDashboard(projectId: string | undefined, dashboardId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.dashboard(projectId ?? '', dashboardId ?? ''),
    queryFn: ({ signal }) => dashboardsApi.get(projectId!, dashboardId!, signal),
    enabled: Boolean(projectId && dashboardId),
    staleTime: 60_000,
  })
}

export function useCreateDashboard(projectId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => dashboardsApi.create(projectId, name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.dashboards(projectId) }),
  })
}

export function useDeleteDashboard(projectId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (dashboardId: string) => dashboardsApi.remove(projectId, dashboardId),
    onSuccess: (_, dashboardId) => {
      queryClient.setQueryData<Dashboard[]>(queryKeys.dashboards(projectId), (current) =>
        current?.filter((dashboard) => dashboard.id !== dashboardId),
      )
      queryClient.removeQueries({ queryKey: queryKeys.dashboard(projectId, dashboardId) })
    },
  })
}

export function useWidgets(projectId: string | undefined, dashboardId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.widgets(projectId ?? '', dashboardId ?? ''),
    queryFn: ({ signal }) => widgetsApi.list(projectId!, dashboardId!, signal),
    enabled: Boolean(projectId && dashboardId),
  })
}

export function useWidgetData(
  projectId: string,
  dashboardId: string,
  widgetId: string,
  options: { refetchInterval?: number | false } = {},
) {
  return useQuery({
    queryKey: queryKeys.widgetData(projectId, dashboardId, widgetId),
    queryFn: ({ signal }) => widgetsApi.data(projectId, dashboardId, widgetId, signal),
    staleTime: 60_000,
    refetchInterval: options.refetchInterval ?? false,
  })
}

export function useCreateWidget(projectId: string, dashboardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateWidgetRequest) => widgetsApi.create(projectId, dashboardId, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.widgets(projectId, dashboardId) }),
  })
}

export function useDeleteWidget(projectId: string, dashboardId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (widgetId: string) => widgetsApi.remove(projectId, dashboardId, widgetId),
    onSuccess: (_, widgetId) => {
      queryClient.setQueryData<Widget[]>(queryKeys.widgets(projectId, dashboardId), (current) =>
        current?.filter((widget) => widget.id !== widgetId),
      )
      queryClient.removeQueries({ queryKey: queryKeys.widgetData(projectId, dashboardId, widgetId) })
    },
  })
}
