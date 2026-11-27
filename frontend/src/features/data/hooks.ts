import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../session/SessionContext";
import { queryKeys } from "../../data/queryKeys";
import type { DatasetListParams } from "../../data/types";
import { normalizeDatasetListParams } from "../../data/pagination";

export function useDatasets(
    projectId: string,
    params: DatasetListParams = {},
    enabled = true,
) {
  const { adapter } = useSession();
  const filters = normalizeDatasetListParams(params);
  const { page, pageSize, search, readyOnly } = filters;

  return useQuery({
    queryKey: queryKeys.datasets.page(
        projectId,
        page,
        pageSize,
        search,
        readyOnly,
    ),
    queryFn: () => adapter.listDatasets(projectId, filters),
    enabled: enabled && Boolean(projectId),
  });
}

export function useDatasetsSummary(projectId: string) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.datasets.summary(projectId),
    queryFn: () => adapter.getDatasetsSummary(projectId),
    enabled: Boolean(projectId),
  });
}

const IMPORT_STATUS_POLL_INTERVAL_MS = 750;

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
    mutationFn: (datasetId: string) => adapter.deleteDataset(projectId, datasetId),

    onSuccess: async (_, datasetId) => {
      // All filtered pages and the project-wide summary share this prefix.
      await queryClient.invalidateQueries({
        queryKey: queryKeys.datasets.list(projectId),
      });

      // Discard cached information derived from the deleted source.
      const removedDatasetKeys = [
        queryKeys.datasets.detail(projectId, datasetId),
        queryKeys.datasets.profile(projectId, datasetId),
        queryKeys.datasets.importStatus(projectId, datasetId),
        ["dataset-rows", projectId, datasetId],
        ["prepared-preview", projectId, datasetId],
        ["chart-data", projectId, datasetId],
        ["exploration", projectId, datasetId],
      ];

      for (const queryKey of removedDatasetKeys) {
        queryClient.removeQueries({ queryKey });
      }

      // Deletion detaches dependent widgets and advances dashboard revisions.
      // Mark editor snapshots/details stale, but do not refetch an active editor:
      // its local unsaved state must not be replaced by a server snapshot.
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ["dashboard", projectId],
          refetchType: "inactive",
        }),
        queryClient.invalidateQueries({
          queryKey: ["dashboard-editor-state", projectId],
          refetchType: "inactive",
        }),
        queryClient.invalidateQueries({
          queryKey: ["widgets", projectId],
        }),
        queryClient.invalidateQueries({
          queryKey: ["widget-data", projectId],
        }),
      ]);
    },
  });
}

export function useDataset(
    projectId: string,
    datasetId: string,
    enabled = true,
) {
  const { adapter } = useSession();

  return useQuery({
    queryKey: queryKeys.datasets.detail(projectId, datasetId),
    queryFn: () => adapter.getDataset(projectId, datasetId),
    enabled: enabled && Boolean(projectId && datasetId),
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
