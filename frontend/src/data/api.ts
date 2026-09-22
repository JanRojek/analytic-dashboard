import type {
  Dashboard,
  DataAdapter,
  Dataset,
  DatasetProfile,
  Project,
  QueryItem,
  User,
  Widget,
} from "./types";

const sessionExpiryListeners = new Set<() => void>();

export function subscribeToSessionExpiry(listener: () => void): () => void {
  sessionExpiryListeners.add(listener);
  return () => {
    sessionExpiryListeners.delete(listener);
  };
}

function reportSessionExpiry(path: string): void {
  if (
    !path.startsWith("/projects") &&
    path !== "/auth/me" &&
    path !== "/auth/logout"
  )
    return;
  for (const listener of sessionExpiryListeners) {
    try {
      listener();
    } catch {
      /* A subscriber must not replace the API error returned to the caller. */
    }
  }
}

export class ApiError extends Error {
  readonly status: number;
  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      credentials: "include",
      signal: AbortSignal.timeout(30_000),
      headers: {
        ...(options.body instanceof FormData
          ? {}
          : { "Content-Type": "application/json" }),
        ...options.headers,
      },
    });
  } catch (error) {
    if (error instanceof Error && error.name === "TimeoutError")
      throw new ApiError(
        "The server took too long to respond. Please try again.",
        0,
      );
    throw new ApiError(
      "Cannot reach the server. Check that the backend is running, then try again.",
      0,
    );
  }
  if (!response.ok) {
    if (response.status === 401) reportSessionExpiry(path);
    const problem = (await response.json().catch(() => null)) as {
      detail?: string;
      title?: string;
      errors?: Record<string, string[]>;
    } | null;
    const validation = problem?.errors
      ? Object.values(problem.errors).flat().join(" ")
      : "";
    const fallback =
      response.status === 401
        ? "Your session has expired. Please sign in again."
        : response.status === 404
          ? "This item was not found or is no longer available."
          : response.status >= 500
            ? "The server could not complete this request. Please try again."
            : "The request could not be completed.";
    throw new ApiError(
      validation || problem?.detail || problem?.title || fallback,
      response.status,
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

const json = (body: unknown, method = "POST"): RequestInit => ({
  method,
  body: JSON.stringify(body),
});
const projectPath = (id: string) => `/projects/${encodeURIComponent(id)}`;
const datasetPath = (p: string, d?: string) =>
  `${projectPath(p)}/datasets${d ? `/${encodeURIComponent(d)}` : ""}`;
const dashboardPath = (p: string, d?: string) =>
  `${projectPath(p)}/dashboards${d ? `/${encodeURIComponent(d)}` : ""}`;
const widgetsPath = (p: string, d: string, w?: string) =>
  `${dashboardPath(p, d)}/widgets${w ? `/${encodeURIComponent(w)}` : ""}`;

export const authApi = {
  login: (email: string, password: string, rememberMe: boolean) =>
    request<void>("/auth/login", json({ email, password, rememberMe })),
  me: () => request<User>("/auth/me"),
  register: (displayName: string, email: string, password: string) =>
    request<User>("/auth/register", json({ displayName, email, password })),
  logout: () => request<void>("/auth/logout", { method: "POST" }),
  confirmEmail: (userId: string, token: string) =>
    request<void>("/auth/confirm-email", json({ userId, token })),
  registrationStatus: () =>
    request<{ status: "Pending" | "Confirmed" }>("/auth/registration-status"),
  completeRegistration: () =>
    request<void>("/auth/complete-registration", { method: "POST" }),
  resendConfirmation: (email: string) =>
    request<void>("/auth/resend-confirmation", json({ email })),
  forgotPassword: (email: string) =>
    request<void>("/auth/forgot-password", json({ email })),
  resetPassword: (userId: string, token: string, newPassword: string) =>
    request<void>("/auth/reset-password", json({ userId, token, newPassword })),
};

export const apiAdapter: DataAdapter = {
  async listProjects() {
    type Page = { items: Project[]; totalPages: number };
    const first = await request<Page>("/projects?page=1&pageSize=100");
    const items = [...first.items];
    for (let page = 2; page <= first.totalPages; page++) {
      items.push(
        ...(await request<Page>(`/projects?page=${page}&pageSize=100`)).items,
      );
    }
    return items;
  },
  getProject: (p) => request<Project>(projectPath(p)),
  createProject: (name) => request<Project>("/projects", json({ name })),
  renameProject: (p, name) =>
    request<Project>(projectPath(p), json({ name }, "PATCH")),
  deleteProject: (p) => request<void>(projectPath(p), { method: "DELETE" }),
  listDatasets: (p) => request<Dataset[]>(datasetPath(p)),
  getDataset: (p, d) => request<Dataset>(datasetPath(p, d)),
  getProfile: (p, d) => request<DatasetProfile>(`${datasetPath(p, d)}/profile`),
  async importCsv(p, file) {
    const body = new FormData();
    body.append("file", file);
    return request<{
      datasetId: string;
      datasetVersionId: string;
      importJobId: string;
    }>(`${datasetPath(p)}/import/csv`, { method: "POST", body });
  },
  deleteDataset: (p, d) =>
    request<void>(datasetPath(p, d), { method: "DELETE" }),
  async query(p, d, config) {
    return (
      await request<{ items: QueryItem[] }>(
        `${datasetPath(p, d)}/query`,
        json(config),
      )
    ).items;
  },
  listDashboards: (p) => request<Dashboard[]>(dashboardPath(p)),
  getDashboard: (p, d) => request<Dashboard>(dashboardPath(p, d)),
  async createDashboard(p, name) {
    const result = await request<{ id: string }>(
      dashboardPath(p),
      json({ name }),
    );
    return request<Dashboard>(dashboardPath(p, result.id));
  },
  deleteDashboard: (p, d) =>
    request<void>(dashboardPath(p, d), { method: "DELETE" }),
  listWidgets: (p, d) => request<Widget[]>(widgetsPath(p, d)),
  async createWidget(p, d, input) {
    const result = await request<{ id: string }>(
      widgetsPath(p, d),
      json(input),
    );
    const widgets = await request<Widget[]>(widgetsPath(p, d));
    const widget = widgets.find((item) => item.id === result.id);
    if (!widget)
      throw new ApiError(
        "The chart was created but could not be reloaded. Refresh the dashboard.",
        0,
      );
    return widget;
  },
  deleteWidget: (p, d, w) =>
    request<void>(widgetsPath(p, d, w), { method: "DELETE" }),
  async widgetData(p, d, w) {
    return (
      await request<{ items: QueryItem[] }>(`${widgetsPath(p, d, w)}/data`)
    ).items;
  },
};
