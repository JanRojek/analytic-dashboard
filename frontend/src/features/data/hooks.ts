import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { datasetsApi } from '../../api/datasets'
import { queryKeys } from '../../api/queryKeys'
import type { Dataset, QueryRequest } from '../../api/types'

export function useDatasets(projectId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.datasets(projectId ?? ''),
    queryFn: ({ signal }) => datasetsApi.list(projectId!, signal),
    enabled: Boolean(projectId),
  })
}

type DatasetOptions = {
  /** Poll while the import is still processing (`currentVersion` null). */
  pollWhilePending?: boolean
}

export function useDataset(
  projectId: string | undefined,
  datasetId: string | undefined,
  options: DatasetOptions = {},
) {
  return useQuery({
    queryKey: queryKeys.dataset(projectId ?? '', datasetId ?? ''),
    queryFn: ({ signal }) => datasetsApi.get(projectId!, datasetId!, signal),
    enabled: Boolean(projectId && datasetId),
    refetchInterval: options.pollWhilePending
      ? (query) => (query.state.data && query.state.data.currentVersion === null ? 1500 : false)
      : false,
  })
}

export function useDatasetProfile(projectId: string | undefined, datasetId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.datasetProfile(projectId ?? '', datasetId ?? ''),
    queryFn: ({ signal }) => datasetsApi.profile(projectId!, datasetId!, signal),
    enabled: Boolean(projectId && datasetId) && enabled,
    staleTime: 5 * 60_000,
  })
}

export function useDatasetQuery(projectId: string | undefined, datasetId: string | undefined, request: QueryRequest | null) {
  return useQuery({
    queryKey: queryKeys.datasetQuery(
      projectId ?? '',
      datasetId ?? '',
      request ?? { groupByColumn: '', measureColumn: '', aggregation: 'Sum' },
    ),
    queryFn: ({ signal }) => datasetsApi.query(projectId!, datasetId!, request!, signal),
    enabled: Boolean(projectId && datasetId && request),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  })
}

export function useDeleteDataset(projectId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (datasetId: string) => datasetsApi.remove(projectId, datasetId),
    onSuccess: (_, datasetId) => {
      queryClient.setQueryData<Dataset[]>(queryKeys.datasets(projectId), (current) =>
        current?.filter((dataset) => dataset.id !== datasetId),
      )
      queryClient.removeQueries({ queryKey: queryKeys.dataset(projectId, datasetId) })
    },
  })
}

/** Marks the dataset list stale after an import so lists pick up the new dataset. */
export function useInvalidateDatasets(projectId: string) {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: queryKeys.datasets(projectId) })
}
