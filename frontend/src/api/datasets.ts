import { request, upload } from './client'
import type {
  Dataset,
  DatasetProfile,
  ImportCsvResponse,
  QueryRequest,
  QueryResponse,
} from './types'

const base = (projectId: string) => `/projects/${projectId}/datasets`

export const datasetsApi = {
  list: (projectId: string, signal?: AbortSignal) =>
    request<Dataset[]>(base(projectId), { signal }),

  get: (projectId: string, datasetId: string, signal?: AbortSignal) =>
    request<Dataset>(`${base(projectId)}/${datasetId}`, { signal }),

  profile: (projectId: string, datasetId: string, signal?: AbortSignal) =>
    request<DatasetProfile>(`${base(projectId)}/${datasetId}/profile`, { signal }),

  query: (projectId: string, datasetId: string, body: QueryRequest, signal?: AbortSignal) =>
    request<QueryResponse>(`${base(projectId)}/${datasetId}/query`, {
      method: 'POST',
      body,
      signal,
    }),

  importCsv: (
    projectId: string,
    file: File,
    onProgress?: (fraction: number) => void,
    signal?: AbortSignal,
  ) =>
    upload<ImportCsvResponse>(`${base(projectId)}/import/csv`, file, 'file', onProgress, signal),

  remove: (projectId: string, datasetId: string) =>
    request<void>(`${base(projectId)}/${datasetId}`, { method: 'DELETE' }),
}
