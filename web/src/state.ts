import type {
  ConnectionState, Conversation, Message, MessagePage, Pending, PresenceEvent, ReadReceipt, TypingEvent,
} from "./types";

/** Messages of one conversation, always sorted by seq without duplicates. */
export type Thread = { messages: Message[]; hasMoreBefore: boolean; loaded: boolean };

export type State = {
  meId: string;
  conversations: Record<string, Conversation>;
  loaded: boolean;
  threads: Record<string, Thread>;
  pending: Pending[];
  /** conversationId → userId → when the "typing" hint expires (ms). */
  typing: Record<string, Record<string, number>>;
  online: Record<string, true>;
  connection: ConnectionState;
};

export const TYPING_VISIBLE_MS = 4000;

export type Action =
  | { type: "conversationsLoaded"; conversations: Conversation[] }
  | { type: "conversationUpserted"; conversation: Conversation }
  | { type: "pageLoaded"; conversationId: string; page: MessagePage; direction: "latest" | "before" | "after" }
  | { type: "messageReceived"; message: Message }
  | { type: "messageUpdated"; message: Message }
  | { type: "pendingAdded"; pending: Pending }
  | { type: "pendingFailed"; clientMessageId: string }
  | { type: "pendingRetried"; clientMessageId: string }
  | { type: "readUpdated"; receipt: ReadReceipt }
  | { type: "typing"; event: TypingEvent; now: number }
  | { type: "typingExpired"; now: number }
  | { type: "presenceLoaded"; online: string[] }
  | { type: "presenceChanged"; event: PresenceEvent }
  | { type: "connectionChanged"; connection: ConnectionState };

export function initialState(meId: string): State {
  return { meId, conversations: {}, loaded: false, threads: {}, pending: [], typing: {}, online: {}, connection: "connecting" };
}

/** Inserts messages by seq; a message already present is replaced (it may have been edited or deleted). */
export function mergeMessages(existing: Message[], incoming: Message[]): Message[] {
  const bySeq = new Map(existing.map((m) => [m.seq, m]));
  for (const message of incoming) bySeq.set(message.seq, message);
  return [...bySeq.values()].sort((a, b) => a.seq - b.seq);
}

/** The highest seq this client has without a hole before it, or 0. Catching up asks for everything after it. */
export function contiguousTail(thread: Thread | undefined): number {
  if (!thread || thread.messages.length === 0) return 0;
  const messages = thread.messages;
  let last = messages[0].seq;
  for (let i = 1; i < messages.length; i++) {
    if (messages[i].seq !== last + 1) return last;
    last = messages[i].seq;
  }
  return last;
}

function withMyRead(conversation: Conversation, meId: string, seq: number): Conversation {
  const members = conversation.members.map((m) =>
    m.userId === meId && m.lastReadSeq < seq ? { ...m, lastReadSeq: seq } : m,
  );
  const mine = members.find((m) => m.userId === meId)?.lastReadSeq ?? 0;
  return { ...conversation, members, unreadCount: Math.max(0, conversation.lastSeq - mine) };
}

export function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "conversationsLoaded":
      return { ...state, loaded: true, conversations: Object.fromEntries(action.conversations.map((c) => [c.id, c])) };

    case "conversationUpserted":
      return { ...state, conversations: { ...state.conversations, [action.conversation.id]: action.conversation } };

    case "pageLoaded": {
      const thread = state.threads[action.conversationId];
      const messages = mergeMessages(thread?.messages ?? [], action.page.messages);
      const hasMoreBefore =
        action.direction === "after" ? (thread?.hasMoreBefore ?? false) : action.direction === "before" || !thread
          ? action.page.hasMore
          : thread.hasMoreBefore;
      return {
        ...state,
        threads: { ...state.threads, [action.conversationId]: { messages, hasMoreBefore, loaded: true } },
      };
    }

    case "messageReceived": {
      const message = action.message;
      const thread = state.threads[message.conversationId];
      const threads = thread
        ? { ...state.threads, [message.conversationId]: { ...thread, messages: mergeMessages(thread.messages, [message]) } }
        : state.threads;
      const pending = state.pending.filter((p) => p.clientMessageId !== message.clientMessageId);
      const conversation = state.conversations[message.conversationId];
      if (!conversation) return { ...state, threads, pending };

      let updated: Conversation = conversation;
      if (message.seq > conversation.lastSeq) {
        updated = { ...conversation, lastSeq: message.seq, lastActivityAt: message.createdAt, lastMessage: message };
      }
      // The sender has read their own message.
      updated = message.senderId === state.meId ? withMyRead(updated, state.meId, message.seq) : withMyRead(updated, state.meId, 0);

      // A message ends that person's typing hint.
      const typingHere = { ...(state.typing[message.conversationId] ?? {}) };
      delete typingHere[message.senderId];

      return {
        ...state,
        threads,
        pending,
        conversations: { ...state.conversations, [updated.id]: updated },
        typing: { ...state.typing, [message.conversationId]: typingHere },
      };
    }

    case "messageUpdated": {
      const message = action.message;
      const thread = state.threads[message.conversationId];
      const conversation = state.conversations[message.conversationId];
      return {
        ...state,
        threads: thread
          ? { ...state.threads, [message.conversationId]: { ...thread, messages: mergeMessages(thread.messages, [message]) } }
          : state.threads,
        conversations:
          conversation && conversation.lastMessage?.id === message.id
            ? { ...state.conversations, [conversation.id]: { ...conversation, lastMessage: message } }
            : state.conversations,
      };
    }

    case "pendingAdded":
      return { ...state, pending: [...state.pending, action.pending] };

    case "pendingFailed":
      return {
        ...state,
        pending: state.pending.map((p) => (p.clientMessageId === action.clientMessageId ? { ...p, failed: true } : p)),
      };

    case "pendingRetried":
      return {
        ...state,
        pending: state.pending.map((p) => (p.clientMessageId === action.clientMessageId ? { ...p, failed: false } : p)),
      };

    case "readUpdated": {
      const { conversationId, userId, seq } = action.receipt;
      const conversation = state.conversations[conversationId];
      if (!conversation) return state;
      const members = conversation.members.map((m) => (m.userId === userId && m.lastReadSeq < seq ? { ...m, lastReadSeq: seq } : m));
      const mine = members.find((m) => m.userId === state.meId)?.lastReadSeq ?? 0;
      return {
        ...state,
        conversations: {
          ...state.conversations,
          [conversationId]: { ...conversation, members, unreadCount: Math.max(0, conversation.lastSeq - mine) },
        },
      };
    }

    case "typing": {
      const { conversationId, userId } = action.event;
      return {
        ...state,
        typing: {
          ...state.typing,
          [conversationId]: { ...(state.typing[conversationId] ?? {}), [userId]: action.now + TYPING_VISIBLE_MS },
        },
      };
    }

    case "typingExpired": {
      let changed = false;
      const typing: State["typing"] = {};
      for (const [conversationId, people] of Object.entries(state.typing)) {
        const alive = Object.fromEntries(Object.entries(people).filter(([, until]) => until > action.now));
        if (Object.keys(alive).length !== Object.keys(people).length) changed = true;
        typing[conversationId] = alive;
      }
      return changed ? { ...state, typing } : state;
    }

    case "presenceLoaded":
      return { ...state, online: Object.fromEntries(action.online.map((id) => [id, true as const])) };

    case "presenceChanged": {
      const online = { ...state.online };
      if (action.event.online) online[action.event.userId] = true;
      else delete online[action.event.userId];
      return { ...state, online };
    }

    case "connectionChanged":
      return { ...state, connection: action.connection };
  }
}

/** Conversations with the most recent activity first. */
export function sortedConversations(state: State): Conversation[] {
  return Object.values(state.conversations).sort((a, b) => b.lastActivityAt.localeCompare(a.lastActivityAt));
}

/** How a message of mine is doing: everyone else has read it, some have, or none yet. */
export function readState(conversation: Conversation, meId: string, seq: number): { read: number; of: number } {
  const others = conversation.members.filter((m) => m.userId !== meId);
  return { read: others.filter((m) => m.lastReadSeq >= seq).length, of: others.length };
}
