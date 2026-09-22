import type { ProblemDetails } from './types'

const API_PREFIX = '/api'

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail?: string
  readonly errors?: Record<string, string[]>

  constructor(status: number, problem: ProblemDetails | undefined, fallback: string) {
    const title = problem?.title?.trim() || fallback
    super(problem?.detail?.trim() || title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = problem?.detail
    this.errors = problem?.errors
  }

  /** First validation message, if the server returned a validation problem. */
  get firstValidationMessage(): string | undefined {
    if (!this.errors) return undefined
    for (const messages of Object.values(this.errors)) {
      if (messages.length > 0) return messages[0]
    }
    return undefined
  }
}

export function describeError(error: unknown, fallback = 'Something went wrong.'): string {
  if (error instanceof ApiError) {
    return error.firstValidationMessage ?? error.detail ?? error.title ?? fallback
  }
  if (error instanceof TypeError) {
    return 'The server could not be reached. Check your connection and try again.'
  }
  if (error instanceof Error && error.message) return error.message
  return fallback
}

type UnauthorizedListener = () => void
const unauthorizedListeners = new Set<UnauthorizedListener>()

/** Called whenever a non-auth request is rejected with 401 (expired session). */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
  /** Suppress the global unauthorized handling (used by the auth endpoints). */
  silentUnauthorized?: boolean
}

async function parseProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return undefined
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const hasBody = options.body !== undefined
  const response = await fetch(`${API_PREFIX}${path}`, {
    method: options.method ?? 'GET',
    headers: hasBody ? { 'Content-Type': 'application/json' } : undefined,
    body: hasBody ? JSON.stringify(options.body) : undefined,
    signal: options.signal,
    credentials: 'same-origin',
  })

  if (!response.ok) {
    const problem = await parseProblem(response)
    if (response.status === 401 && !options.silentUnauthorized) {
      unauthorizedListeners.forEach((listener) => listener())
    }
    throw new ApiError(response.status, problem, response.statusText || 'Request failed')
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/**
 * Multipart upload with progress reporting. `fetch` has no upload progress, so
 * this uses XMLHttpRequest; the response is treated like any other API response.
 */
export function upload<T>(
  path: string,
  file: File,
  fieldName: string,
  onProgress?: (fraction: number) => void,
  signal?: AbortSignal,
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${API_PREFIX}${path}`)
    xhr.withCredentials = true
    xhr.responseType = 'text'

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && onProgress) onProgress(event.loaded / event.total)
    }

    xhr.onload = () => {
      const status = xhr.status
      if (status >= 200 && status < 300) {
        onProgress?.(1)
        resolve((xhr.responseText ? JSON.parse(xhr.responseText) : undefined) as T)
        return
      }
      let problem: ProblemDetails | undefined
      try {
        problem = xhr.responseText ? (JSON.parse(xhr.responseText) as ProblemDetails) : undefined
      } catch {
        problem = undefined
      }
      if (status === 401) unauthorizedListeners.forEach((listener) => listener())
      reject(new ApiError(status, problem, xhr.statusText || 'Upload failed'))
    }

    xhr.onerror = () => reject(new TypeError('Network error during upload'))
    xhr.onabort = () => reject(new DOMException('Upload aborted', 'AbortError'))

    signal?.addEventListener('abort', () => xhr.abort(), { once: true })

    const form = new FormData()
    form.append(fieldName, file, file.name)
    xhr.send(form)
  })
}
