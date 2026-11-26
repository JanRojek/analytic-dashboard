import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../session/SessionContext";
import { queryKeys } from "../../data/queryKeys";
import { normalizePagination } from "../../data/pagination";
import type {
  CreateWidgetInput,
  DashboardWidgetUpdate,
  PaginationParams,
} from "../../data/types";

export function useDashboards(
  projectId: string,
  params: PaginationParams = {},
  enabled = true,
) {
  const { adapter } = useSession();
  const pagination = normalizePagination(params);

  return useQuery({
    queryKey: queryKeys.dashboards.page(
      projectId,
      pagination.page,
      pagination.pageSize,
    ),
    queryFn: () => adapter.listDashboards(projectId, pagination),
    enabled: enabled && Boolean(projectId),
  });
}

export function useDashboard(
  projectId: string,
  dashboardId: string,
  enabled = true,
) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.dashboards.detail(projectId, dashboardId),
    queryFn: () => adapter.getDashboard(projectId, dashboardId),
    enabled: enabled && Boolean(projectId && dashboardId),
  });
}

export function useDashboardEditorState(
  projectId: string,
  dashboardId: string,
) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.dashboards.editorState(projectId, dashboardId),
    queryFn: () => adapter.getDashboardEditorState(projectId, dashboardId),
    enabled: Boolean(projectId && dashboardId),
    // Every navigation to Edit/Preview must start from a fresh server snapshot.
    // Do not refetch unexpectedly while the user is editing a local document.
    refetchOnMount: "always",
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
}

export function useDashboardWidgets(
  projectId: string,
  dashboardId: string | undefined,
) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
    queryFn: () => adapter.listWidgets(projectId, dashboardId!),
    enabled: Boolean(dashboardId),
  });
}

export function useCreateDashboard(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => adapter.createDashboard(projectId, name),

    onSuccess: async (dashboard) => {
      queryClient.setQueryData(
        queryKeys.dashboards.detail(projectId, dashboard.id),
        dashboard,
      );
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.list(projectId),
      });
    },
  });
}

export function useCreateWidget(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      dashboardId,
      input,
    }: {
      dashboardId: string;
      input: CreateWidgetInput;
    }) => adapter.createWidget(projectId, dashboardId, input),

    onSuccess: async (_, { dashboardId }) => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
      });
    },
  });
}

export function useDeleteDashboard(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (dashboardId: string) =>
      adapter.deleteDashboard(projectId, dashboardId),

    onSuccess: async (_, dashboardId) => {
      queryClient.removeQueries({
        queryKey: queryKeys.dashboards.detail(projectId, dashboardId),
      });
      queryClient.removeQueries({
        queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
      });
      queryClient.removeQueries({
        queryKey: queryKeys.dashboards.editorState(projectId, dashboardId),
      });
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.list(projectId),
      });
    },
  });
}

export function useDeleteWidget(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      dashboardId,
      widgetId,
    }: {
      dashboardId: string;
      widgetId: string;
    }) => adapter.deleteWidget(projectId, dashboardId, widgetId),

    onSuccess: async (_, { dashboardId }) => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
      });
    },
  });
}

export function useUpdateDashboardWidgets(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      dashboardId,
      updates,
      expectedRevision,
    }: {
      dashboardId: string;
      updates: DashboardWidgetUpdate[];
      expectedRevision: number;
    }) =>
      adapter.updateDashboardWidgets(
        projectId,
        dashboardId,
        updates,
        expectedRevision,
      ),

    onSuccess: (_, { dashboardId }) => {
      // Editor state is updated atomically by useDashboardAutosave, which knows
      // the exact submitted widgets and the acknowledged revision.
      // Legacy independent read consumers may still refresh separately.
      void queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.detail(projectId, dashboardId),
      });

      void queryClient.invalidateQueries({
        queryKey: queryKeys.dashboards.widgets(projectId, dashboardId),
      });
    },
  });
}
