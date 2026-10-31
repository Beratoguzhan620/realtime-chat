import { MessagesSquare, WifiOff } from "lucide-react";
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { api } from "../api";
import { ChatConnection } from "../connection";
import { contiguousTail, initialState, reducer, type State } from "../state";
import type { Message, Pending, Session } from "../types";
import { ConversationView } from "./ConversationView";
import { NewConversation } from "./NewConversation";
import { Sidebar } from "./Sidebar";

export function ChatApp({ session, onSignOut }: { session: Session; onSignOut: () => void }) {
  const [state, dispatch] = useReducer(reducer, session.user.id, initialState);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [threadErrors, setThreadErrors] = useState<Record<string, string>>({});
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  // Handlers registered once on the connection read the latest state through this ref.
  const stateRef = useRef<State>(state);
  stateRef.current = state;
  const activeRef = useRef(activeId);
  activeRef.current = activeId;
  const connection = useRef<ChatConnection | null>(null);

  const loadConversations = useCallback(async () => {
    try {
      dispatch({ type: "conversationsLoaded", conversations: await api.conversations() });
      setLoadError(null);
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : "Could not load conversations.");
    }
  }, []);

  const loadLatest = useCallback(async (conversationId: string) => {
    try {
      const page = await api.messages(conversationId);
      dispatch({ type: "pageLoaded", conversationId, page, direction: "latest" });
      setThreadErrors(({ [conversationId]: _, ...rest }) => rest);
    } catch (e) {
      setThreadErrors((errors) => ({ ...errors, [conversationId]: e instanceof Error ? e.message : "Could not load messages." }));
    }
  }, []);

  /** Fetches everything after the last message this client has without a gap, page by page. */
  const catchUp = useCallback(async (conversationId: string) => {
    let after = contiguousTail(stateRef.current.threads[conversationId]);
    for (;;) {
      const page = await api.messages(conversationId, { after, limit: 200 });
      dispatch({ type: "pageLoaded", conversationId, page, direction: "after" });
      if (!page.hasMore || page.messages.length === 0) break;
      after = page.messages[page.messages.length - 1].seq;
    }
  }, []);

  const refreshPresence = useCallback(async () => {
    try {
      dispatch({ type: "presenceLoaded", online: await api.presence() });
    } catch {
      // Presence is a nicety; the next change event corrects it.
    }
  }, []);

  const sendPending = useCallback(async (pending: Pending) => {
    try {
      const message = await connection.current!.send(pending.conversationId, pending.clientMessageId, pending.body);
      dispatch({ type: "messageReceived", message });
    } catch (e) {
      dispatch({ type: "pendingFailed", clientMessageId: pending.clientMessageId });
      setNotice(e instanceof Error ? e.message : "The message could not be sent.");
    }
  }, []);

  useEffect(() => {
    const chat = new ChatConnection(() => session.accessToken, {
      messageReceived: (message) => {
        const thread = stateRef.current.threads[message.conversationId];
        dispatch({ type: "messageReceived", message });
        if (!stateRef.current.conversations[message.conversationId]) {
          void api.conversation(message.conversationId).then((c) => dispatch({ type: "conversationUpserted", conversation: c }));
        }
        // A number was skipped: something arrived while this tab was not listening.
        if (thread?.loaded && message.seq > contiguousTail(thread) + 1) void catchUp(message.conversationId);
      },
      messageUpdated: (message) => dispatch({ type: "messageUpdated", message }),
      readUpdated: (receipt) => dispatch({ type: "readUpdated", receipt }),
      typing: (event) => dispatch({ type: "typing", event, now: Date.now() }),
      presenceChanged: (event) => dispatch({ type: "presenceChanged", event }),
      conversationChanged: (id) => {
        // Someone new to talk to: their presence matters now.
        void refreshPresence();
        void api.conversation(id)
          .then((conversation) => dispatch({ type: "conversationUpserted", conversation }))
          .catch(() => void loadConversations());
      },
      stateChanged: (value) => dispatch({ type: "connectionChanged", connection: value }),
      connected: () => {
        // After a (re)connect: refresh the list and presence, fetch what was missed, resend what failed.
        void loadConversations();
        void refreshPresence();
        for (const [id, thread] of Object.entries(stateRef.current.threads)) {
          if (thread.loaded) void catchUp(id).catch(() => {});
        }
        for (const pending of stateRef.current.pending.filter((p) => p.failed)) {
          dispatch({ type: "pendingRetried", clientMessageId: pending.clientMessageId });
          void sendPending(pending);
        }
      },
    });
    connection.current = chat;
    void chat.start();
    return () => void chat.stop();
  }, [session.accessToken, catchUp, loadConversations, refreshPresence, sendPending]);

  useEffect(() => {
    void loadConversations();
  }, [loadConversations]);

  useEffect(() => {
    const timer = setInterval(() => dispatch({ type: "typingExpired", now: Date.now() }), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(() => setNotice(null), 5000);
    return () => clearTimeout(timer);
  }, [notice]);

  const active = activeId ? state.conversations[activeId] : undefined;

  useEffect(() => {
    if (activeId && !state.threads[activeId]?.loaded) void loadLatest(activeId);
  }, [activeId, state.threads, loadLatest]);

  // Reading: when the open conversation has unread messages and the window is visible, mark them read once.
  const markedRead = useRef<Record<string, number>>({});
  useEffect(() => {
    if (!active || active.unreadCount === 0 || state.connection !== "connected" || document.hidden) return;
    if (!state.threads[active.id]?.loaded || (markedRead.current[active.id] ?? 0) >= active.lastSeq) return;
    markedRead.current[active.id] = active.lastSeq;
    void connection.current?.markRead(active.id, active.lastSeq).catch(() => {
      markedRead.current[active.id] = 0;
    });
  }, [active, state.connection, state.threads]);

  useEffect(() => {
    const visible = () => {
      const current = activeRef.current ? stateRef.current.conversations[activeRef.current] : undefined;
      if (!document.hidden && current && current.unreadCount > 0) void connection.current?.markRead(current.id, current.lastSeq).catch(() => {});
    };
    document.addEventListener("visibilitychange", visible);
    return () => document.removeEventListener("visibilitychange", visible);
  }, []);

  const send = useCallback((body: string) => {
    if (!activeRef.current) return;
    const pending: Pending = {
      clientMessageId: newId(),
      conversationId: activeRef.current,
      body,
      createdAt: new Date().toISOString(),
      failed: false,
    };
    dispatch({ type: "pendingAdded", pending });
    void sendPending(pending);
  }, [sendPending]);

  const retry = useCallback((pending: Pending) => {
    dispatch({ type: "pendingRetried", clientMessageId: pending.clientMessageId });
    void sendPending(pending);
  }, [sendPending]);

  const edit = useCallback(async (message: Message, body: string) => {
    dispatch({ type: "messageUpdated", message: await connection.current!.edit(message.id, body) });
  }, []);

  const remove = useCallback(async (message: Message) => {
    try {
      dispatch({ type: "messageUpdated", message: await connection.current!.remove(message.id) });
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "The message could not be deleted.");
    }
  }, []);

  const loadOlder = useCallback(async () => {
    const id = activeRef.current;
    const first = id ? stateRef.current.threads[id]?.messages[0] : undefined;
    if (!id || !first) return;
    setLoadingOlder(true);
    try {
      dispatch({ type: "pageLoaded", conversationId: id, page: await api.messages(id, { before: first.seq }), direction: "before" });
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Could not load older messages.");
    } finally {
      setLoadingOlder(false);
    }
  }, []);

  const me = useMemo(() => session.user, [session.user]);

  return (
    <div className={`app${active ? " has-active" : ""}`}>
      <Sidebar
        state={state}
        me={me}
        activeId={activeId}
        loadError={loadError}
        onOpen={setActiveId}
        onNew={() => setShowNew(true)}
        onRetry={() => void loadConversations()}
        onSignOut={onSignOut}
      />
      <main className="main">
        {state.connection === "reconnecting" && (
          <div className="banner" role="status"><WifiOff size={15} aria-hidden /> Connection lost. Reconnecting; nothing you send will be lost.</div>
        )}
        {active ? (
          <ConversationView
            state={state}
            conversation={active}
            loadingOlder={loadingOlder}
            threadError={threadErrors[active.id] ?? null}
            onBack={() => setActiveId(null)}
            onSend={send}
            onRetry={retry}
            onEdit={edit}
            onDelete={remove}
            onTyping={() => connection.current?.typing(active.id)}
            onLoadOlder={() => void loadOlder()}
            onReload={() => void loadLatest(active.id)}
          />
        ) : (
          <div className="empty-main">
            <MessagesSquare size={36} aria-hidden />
            <p><strong>Pick a conversation</strong></p>
            <p className="muted">Or start a new one with the New button.</p>
          </div>
        )}
        {notice && <div className="toast" role="alert">{notice}</div>}
      </main>
      {showNew && (
        <NewConversation
          onClose={() => setShowNew(false)}
          onCreated={(conversation) => {
            dispatch({ type: "conversationUpserted", conversation });
            void refreshPresence();
            setActiveId(conversation.id);
            setShowNew(false);
          }}
        />
      )}
    </div>
  );
}

/** A random id for a new message. randomUUID needs a secure context, so plain http falls back to random bytes. */
function newId(): string {
  if (typeof crypto.randomUUID === "function" && window.isSecureContext) return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
