export type Priority = "low" | "normal" | "high";

export interface Task {
  id: number;
  title: string;
  notes: string | null;
  dueDate: string | null;
  priority: Priority;
  done: boolean;
  createdAt: string;
  updatedAt: string;
  version: number;
}

export interface TaskInput {
  title: string;
  notes?: string | null;
  dueDate?: string | null;
  priority?: Priority;
}

export interface TaskUpdate extends TaskInput {
  done: boolean;
  version: number;
}

export interface ServerInfo {
  mode: "demo" | "anthropic";
  model: string | null;
  demoMode: boolean;
  pendingActionMinutes: number;
  maxBulkDelete: number;
  readTools: string[];
  writeTools: string[];
}

export type ActionStatus = "pending" | "executed" | "rejected" | "expired" | "stale" | "failed";

export interface PendingAction {
  id: string;
  conversationId: string;
  tool: string;
  preview: string;
  targets: { id: number; title: string; version: number }[];
  arguments: Record<string, unknown>;
  status: ActionStatus;
  createdAt: string;
  expiresAt: string;
  resolvedAt: string | null;
  resultIsError: boolean | null;
  result: string | null;
}

export interface ChatMessage {
  role: "user" | "assistant";
  text: string;
}

export interface ChatResponse {
  conversationId: string;
  mode: string;
  messages: ChatMessage[];
  steps: { tool: string; kind: "read" | "write" | "unknown"; outcome: "executed" | "proposed" | "error" }[];
  pendingActions: PendingAction[];
}

export interface ActionResolution {
  action: PendingAction;
  chat: ChatResponse | null;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  currentVersion?: number;
  action?: PendingAction;
}
