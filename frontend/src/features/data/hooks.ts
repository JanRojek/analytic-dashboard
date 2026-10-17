import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../session/SessionContext";
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
        count: widgets.filter((widget) => widget.datasetId === datasetId)
          .length,
      };
    }),
  );

  return usages.filter((usage) => usage.count > 0);
}

export function useDatasets(projectId: string, enabled = true) {
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

const IMPORT_STATUS_POLL_INTERVAL_MS = 3000;

export function useImportStatus(
  projectId: string,
  datasetId: string,
  enabled = true,
) {
  const { adapter, mode } = useSession();

  return useQuery({
    queryKey: queryKeys.datasets.importStatus(projectId, datasetId),
    queryFn: () => adapter.getImportStatus(projectId, datasetId),

    enabled: mode === "api" && enabled && Boolean(projectId && datasetId),

    refetchInterval: (query) => {
      if (query.state.error) return false;

      const status = query.state.data?.status;

      if (status === "Completed" || status === "Failed") {
        return false;
      }

      return IMPORT_STATUS_POLL_INTERVAL_MS;
    },
  });
}

export function useDeleteDataset(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (datasetId: string) => {
      const references = await findDatasetUsage(adapter, projectId, datasetId);

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

export function useDataset(projectId: string, datasetId: string) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.datasets.detail(projectId, datasetId),
    queryFn: () => adapter.getDataset(projectId, datasetId),
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

export function useDatasetRows(
  projectId: string,
  datasetId: string,
  page: number,
  pageSize: number,
  enabled = true,
) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.datasets.rows(projectId, datasetId, page, pageSize),
    queryFn: () => adapter.getDatasetRows(projectId, datasetId, page, pageSize),
    enabled,
  });
}

export function useImportDataset(projectId: string) {
  const { adapter } = useSession();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (upload: File) => adapter.importCsv(projectId, upload),

    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: queryKeys.datasets.list(projectId),
      });
    },
  });
}
