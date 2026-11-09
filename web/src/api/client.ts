import type {
  ActionResolution,
  ChatResponse,
  PendingAction,
  ProblemDetails,
  ServerInfo,
  Task,
  TaskInput,
  TaskUpdate,
} from "./types";

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(ApiError.describe(status, problem));
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  private static describe(status: number, problem: ProblemDetails): string {
    const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : [];
    if (fieldErrors.length > 0) return fieldErrors.join(" ");
    return problem.detail ?? problem.title ?? `Erro ${status} na API.`;
  }
}

type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

export function createClient(baseUrl = "/api", fetchFn: FetchLike = (i, init) => fetch(i, init)) {
  async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const init: RequestInit = { method, headers: { Accept: "application/json" } };
    if (body !== undefined) {
      init.headers = { ...init.headers, "Content-Type": "application/json" };
      init.body = JSON.stringify(body);
    }

    let response: Response;
    try {
      response = await fetchFn(baseUrl + path, init);
    } catch {
      throw new ApiError(0, { title: "Não foi possível falar com a API." });
    }

    if (response.status === 204) return undefined as T;

    const text = await response.text();
    let data: unknown = undefined;
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = undefined;
      }
    }

    if (!response.ok) {
      throw new ApiError(response.status, (data as ProblemDetails | undefined) ?? {});
    }
    return data as T;
  }

  return {
    info: () => request<ServerInfo>("GET", "/info"),

    listTasks: (status: "all" | "open" | "done" = "all") =>
      request<Task[]>("GET", `/tasks?status=${encodeURIComponent(status)}`),
    createTask: (input: TaskInput) => request<Task>("POST", "/tasks", input),
    updateTask: (id: number, update: TaskUpdate) => request<Task>("PUT", `/tasks/${id}`, update),
    deleteTask: (id: number, version: number) =>
      request<void>("DELETE", `/tasks/${id}?version=${version}`),

    chat: (message: string, conversationId: string | null) =>
      request<ChatResponse>("POST", "/chat", { conversationId, message }),
    confirmAction: (id: string) =>
      request<ActionResolution>("POST", `/actions/${encodeURIComponent(id)}/confirm`),
    rejectAction: (id: string) =>
      request<ActionResolution>("POST", `/actions/${encodeURIComponent(id)}/reject`),
    getAction: (id: string) => request<PendingAction>("GET", `/actions/${encodeURIComponent(id)}`),
  };
}

export type ApiClient = ReturnType<typeof createClient>;

export const api = createClient();
