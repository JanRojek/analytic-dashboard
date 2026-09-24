import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../data/session";
import { queryKeys } from "../../data/queryKeys";
import type { WidgetInput } from "../../data/types";

export function useDashboards(projectId: string, enabled = true) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.dashboards.list(projectId),
        queryFn: () => adapter.listDashboards(projectId),
        enabled,
    });
}

export function useDashboard(
    projectId: string,
    dashboardId: string,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.dashboards.detail(projectId, dashboardId),
        queryFn: () => adapter.getDashboard(projectId, dashboardId),
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
        mutationFn: (name: string) =>
            adapter.createDashboard(projectId, name),

        onSuccess: async () => {
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
            input: WidgetInput;
        }) => adapter.createWidget(projectId, dashboardId, input),

        onSuccess: async (_, { dashboardId }) => {
            await queryClient.invalidateQueries({
                queryKey: queryKeys.dashboards.widgets(
                    projectId,
                    dashboardId,
                ),
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

        onSuccess: async () => {
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
