import type { Conversation, Message, MessagePage, Session, User } from "./types";

/** An API error with the stable code from the problem response, such as "email_taken". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch {
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? "Something went wrong. Try again.";
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);

export const api = {
  register: (email: string, displayName: string, password: string) =>
    request<Session>("/api/auth/register", { method: "POST", body: json({ email, displayName, password }) }),
  login: (email: string, password: string) =>
    request<Session>("/api/auth/login", { method: "POST", body: json({ email, password }) }),
  conversations: () => request<Conversation[]>("/api/conversations"),
  conversation: (id: string) => request<Conversation>(`/api/conversations/${id}`),
  searchUsers: (query: string) => request<User[]>(`/api/users?query=${encodeURIComponent(query)}`),
  startDirect: (userId: string) =>
    request<Conversation>("/api/conversations/direct", { method: "POST", body: json({ userId }) }),
  startGroup: (title: string, memberIds: string[]) =>
    request<Conversation>("/api/conversations/groups", { method: "POST", body: json({ title, memberIds }) }),
  messages: (conversationId: string, params: { before?: number; after?: number; limit?: number } = {}) => {
    const query = new URLSearchParams();
    if (params.before !== undefined) query.set("before", String(params.before));
    if (params.after !== undefined) query.set("after", String(params.after));
    query.set("limit", String(params.limit ?? 50));
    return request<MessagePage>(`/api/conversations/${conversationId}/messages?${query}`);
  },
  presence: () => request<string[]>("/api/presence"),
  editMessage: (id: string, body: string) => request<Message>(`/api/messages/${id}`, { method: "PATCH", body: json({ body }) }),
};
