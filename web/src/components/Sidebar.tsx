import { LogOut, MessageSquarePlus, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { formatListTime } from "../format";
import type { State } from "../state";
import { sortedConversations } from "../state";
import type { Conversation, User } from "../types";
import { Avatar } from "./Avatar";

type Props = {
  state: State;
  me: User;
  activeId: string | null;
  loadError: string | null;
  onOpen: (id: string) => void;
  onNew: () => void;
  onRetry: () => void;
  onSignOut: () => void;
};

export function Sidebar({ state, me, activeId, loadError, onOpen, onNew, onRetry, onSignOut }: Props) {
  const [filter, setFilter] = useState("");
  const conversations = useMemo(() => {
    const all = sortedConversations(state);
    const text = filter.trim().toLocaleLowerCase("tr-TR");
    return text ? all.filter((c) => c.title.toLocaleLowerCase("tr-TR").includes(text)) : all;
  }, [state, filter]);

  return (
    <aside className="sidebar" aria-label="Conversations">
      <header className="sidebar-header">
        <div className="me">
          <Avatar name={me.displayName} size={34} />
          <div>
            <strong>{me.displayName}</strong>
            <ConnectionLabel state={state} />
          </div>
        </div>
        <button className="icon-button" onClick={onSignOut} aria-label="Sign out" title="Sign out">
          <LogOut size={18} />
        </button>
      </header>

      <div className="sidebar-tools">
        <label className="search">
          <Search size={16} aria-hidden />
          <input placeholder="Filter conversations" value={filter} onChange={(e) => setFilter(e.target.value)} aria-label="Filter conversations" />
        </label>
        <button className="button primary" onClick={onNew}>
          <MessageSquarePlus size={16} aria-hidden /> New
        </button>
      </div>

      <nav className="conversation-list">
        {!state.loaded && !loadError && <ListSkeleton />}
        {loadError && (
          <div className="state-box error">
            <p>{loadError}</p>
            <button className="button" onClick={onRetry}>Try again</button>
          </div>
        )}
        {state.loaded && Object.keys(state.conversations).length === 0 && (
          <div className="state-box">
            <p><strong>No conversations yet</strong></p>
            <p className="muted">Start one with someone by name or email.</p>
            <button className="button primary" onClick={onNew}>Start a conversation</button>
          </div>
        )}
        {state.loaded && conversations.length === 0 && Object.keys(state.conversations).length > 0 && (
          <p className="muted padded">Nothing matches “{filter}”.</p>
        )}
        {conversations.map((c) => (
          <ConversationRow key={c.id} conversation={c} state={state} active={c.id === activeId} onOpen={onOpen} />
        ))}
      </nav>
    </aside>
  );
}

function ConversationRow({ conversation, state, active, onOpen }: { conversation: Conversation; state: State; active: boolean; onOpen: (id: string) => void }) {
  const other = conversation.kind === "Direct" ? conversation.members.find((m) => m.userId !== state.meId) : undefined;
  const typing = Object.keys(state.typing[conversation.id] ?? {}).filter((id) => id !== state.meId);
  const last = conversation.lastMessage;
  const sender = last && last.senderId === state.meId ? "You: " : last && conversation.kind === "Group"
    ? `${conversation.members.find((m) => m.userId === last.senderId)?.displayName.split(" ")[0] ?? "Someone"}: `
    : "";
  const preview = typing.length > 0 ? "typing…" : last ? (last.deletedAt ? "Message deleted" : sender + last.body) : "No messages yet";

  return (
    <button
      className={`conversation-row${active ? " active" : ""}${conversation.unreadCount > 0 ? " unread" : ""}`}
      onClick={() => onOpen(conversation.id)}
      aria-current={active ? "true" : undefined}
      data-conversation={conversation.title}
    >
      <Avatar name={conversation.title} group={conversation.kind === "Group"} online={other ? !!state.online[other.userId] : false} />
      <span className="row-text">
        <span className="row-top">
          <span className="row-title">{conversation.title}</span>
          <span className="row-time">{formatListTime(last?.createdAt ?? conversation.lastActivityAt)}</span>
        </span>
        <span className="row-bottom">
          <span className={`row-preview${typing.length > 0 ? " typing" : ""}`}>{preview}</span>
          {conversation.unreadCount > 0 && (
            <span className="badge" aria-label={`${conversation.unreadCount} unread`}>{conversation.unreadCount > 99 ? "99+" : conversation.unreadCount}</span>
          )}
        </span>
      </span>
    </button>
  );
}

function ConnectionLabel({ state }: { state: State }) {
  const labels = { connected: "Online", connecting: "Connecting…", reconnecting: "Reconnecting…", disconnected: "Offline" };
  return <span className={`connection ${state.connection}`}>{labels[state.connection]}</span>;
}

function ListSkeleton() {
  return (
    <div aria-label="Loading conversations" aria-busy="true">
      {[0, 1, 2, 3].map((i) => (
        <div key={i} className="skeleton-row">
          <span className="skeleton circle" />
          <span className="skeleton-lines"><span className="skeleton line" /><span className="skeleton line short" /></span>
        </div>
      ))}
    </div>
  );
}
