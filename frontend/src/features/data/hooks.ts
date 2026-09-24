import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../data/session";
import { queryKeys } from "../../data/queryKeys";
import type { DataAdapter } from "../../data/types";

async function findDatasetUsage(
    adapter: DataAdapter,
    projectId: string,
    datasetId: string,
) {
    const dashboards = await adapter.listDashboards(projectId);

    const usages = await Promise.all(
        dashboards.map(async (dashboard) => {
            const widgets = await adapter.listWidgets(projectId, dashboard.id);

            return {
                dashboard,
                count: widgets.filter((widget) => widget.datasetId === datasetId).length,
            };
        }),
    );

    return usages.filter((usage) => usage.count > 0);
}

export function useDatasets(
    projectId: string,
    enabled = true,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.datasets.list(projectId),
        queryFn: () => adapter.listDatasets(projectId),
        enabled,
    });
}

export function useDatasetUsage(
    projectId: string,
    datasetId: string | undefined,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.datasets.usage(projectId, datasetId),
        queryFn: () => findDatasetUsage(adapter, projectId, datasetId!),
        enabled: Boolean(datasetId),
        staleTime: 0,
    });
}

export function useDeleteDataset(projectId: string) {
    const { adapter } = useSession();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (datasetId: string) => {
            const references = await findDatasetUsage(
                adapter,
                projectId,
                datasetId,
            );

            if (references.length) {
                throw new Error(
                    "This dataset is used by a dashboard. Remove its charts before deleting it.",
                );
            }

            return adapter.deleteDataset(projectId, datasetId);
        },

        onSuccess: async (_, datasetId) => {
            await queryClient.invalidateQueries({
                queryKey: queryKeys.datasets.list(projectId),
            });

            queryClient.removeQueries({
                queryKey: queryKeys.datasets.detail(projectId, datasetId),
            });

            queryClient.removeQueries({
                queryKey: queryKeys.datasets.profile(projectId, datasetId),
            });
        },
    });
}

export function useDataset(
    projectId: string,
    datasetId: string,
    pollingStarted: number,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.datasets.detail(projectId, datasetId),
        queryFn: () => adapter.getDataset(projectId, datasetId),

        refetchInterval: (query) =>
            !query.state.data?.currentVersion &&
            Date.now() - pollingStarted < 60_000 &&
            !query.state.error
                ? 2500
                : false,
    });
}

export function useDatasetProfile(
    projectId: string,
    datasetId: string,
    enabled = true,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.datasets.profile(projectId, datasetId),
        queryFn: () => adapter.getProfile(projectId, datasetId),
        enabled,
    });
}

export function useImportDataset(projectId: string) {
    const { adapter } = useSession();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (upload: File) =>
            adapter.importCsv(projectId, upload),

        onSuccess: async () => {
            await queryClient.invalidateQueries({
                queryKey: queryKeys.datasets.list(projectId),
            });
        },
    });
}
