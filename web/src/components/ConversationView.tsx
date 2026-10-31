import { AlertCircle, ArrowLeft, Check, CheckCheck, Clock, LoaderCircle, Pencil, RotateCw, SendHorizontal, Trash2, X } from "lucide-react";
import { Fragment, useEffect, useLayoutEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { formatDay, formatTime } from "../format";
import type { State } from "../state";
import { readState } from "../state";
import type { Conversation, Message, Pending } from "../types";
import { Avatar } from "./Avatar";

type Props = {
  state: State;
  conversation: Conversation;
  loadingOlder: boolean;
  threadError: string | null;
  onBack: () => void;
  onSend: (body: string) => void;
  onRetry: (pending: Pending) => void;
  onEdit: (message: Message, body: string) => Promise<void>;
  onDelete: (message: Message) => Promise<void>;
  onTyping: () => void;
  onLoadOlder: () => void;
  onReload: () => void;
};

export function ConversationView(props: Props) {
  const { state, conversation, onBack } = props;
  const others = conversation.members.filter((m) => m.userId !== state.meId);
  const typingNames = Object.keys(state.typing[conversation.id] ?? {})
    .filter((id) => id !== state.meId)
    .map((id) => conversation.members.find((m) => m.userId === id)?.displayName.split(" ")[0] ?? "Someone");

  const subtitle = conversation.kind === "Direct"
    ? (state.online[others[0]?.userId ?? ""] ? "Online" : "Offline")
    : `${conversation.members.length} members · ${others.filter((m) => state.online[m.userId]).length} online`;

  return (
    <section className="conversation" aria-label={conversation.title}>
      <header className="conversation-header">
        <button className="icon-button back" onClick={onBack} aria-label="Back to conversations"><ArrowLeft size={18} /></button>
        <Avatar
          name={conversation.title}
          group={conversation.kind === "Group"}
          online={conversation.kind === "Direct" && !!state.online[others[0]?.userId ?? ""]}
          size={38}
        />
        <div className="conversation-heading">
          <h2>{conversation.title}</h2>
          <span className="muted small" aria-live="polite">
            {typingNames.length > 0 ? <span className="typing-text">{typingLabel(typingNames)}</span> : subtitle}
          </span>
        </div>
      </header>

      <MessageList {...props} />
      <Composer key={conversation.id} onSend={props.onSend} onTyping={props.onTyping} disabled={state.connection !== "connected"} />
    </section>
  );
}

function typingLabel(names: string[]) {
  if (names.length === 1) return `${names[0]} is typing…`;
  if (names.length === 2) return `${names[0]} and ${names[1]} are typing…`;
  return "Several people are typing…";
}

function MessageList({ state, conversation, loadingOlder, threadError, onRetry, onEdit, onDelete, onLoadOlder, onReload }: Props) {
  const thread = state.threads[conversation.id];
  const pending = state.pending.filter((p) => p.conversationId === conversation.id);
  const scroller = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);
  const [editing, setEditing] = useState<string | null>(null);
  const count = (thread?.messages.length ?? 0) + pending.length;

  useLayoutEffect(() => {
    const element = scroller.current;
    if (element && stickToBottom.current) element.scrollTop = element.scrollHeight;
  }, [count, conversation.id, state.typing]);

  const onScroll = () => {
    const element = scroller.current;
    if (!element) return;
    stickToBottom.current = element.scrollHeight - element.scrollTop - element.clientHeight < 80;
    if (element.scrollTop < 60 && thread?.hasMoreBefore && !loadingOlder) onLoadOlder();
  };

  if (threadError && !thread?.loaded) {
    return (
      <div className="messages center">
        <div className="state-box error">
          <AlertCircle size={20} aria-hidden />
          <p>{threadError}</p>
          <button className="button" onClick={onReload}>Try again</button>
        </div>
      </div>
    );
  }

  if (!thread?.loaded) {
    return (
      <div className="messages center" aria-busy="true">
        <LoaderCircle size={22} className="spin muted" aria-label="Loading messages" />
      </div>
    );
  }

  if (thread.messages.length === 0 && pending.length === 0) {
    return (
      <div className="messages center">
        <div className="state-box">
          <p><strong>No messages yet</strong></p>
          <p className="muted">Say hello to {conversation.kind === "Direct" ? conversation.title : "the group"}.</p>
        </div>
      </div>
    );
  }

  let previousDay = "";
  let previousSender = "";
  return (
    <div className="messages" ref={scroller} onScroll={onScroll} role="log" aria-label="Messages">
      {thread.hasMoreBefore && (
        <div className="load-older">
          {loadingOlder ? <LoaderCircle size={16} className="spin muted" aria-label="Loading older messages" /> : (
            <button className="link" onClick={onLoadOlder}>Load older messages</button>
          )}
        </div>
      )}
      {thread.messages.map((message) => {
        const day = formatDay(message.createdAt);
        const newDay = day !== previousDay;
        const grouped = !newDay && previousSender === message.senderId;
        previousDay = day;
        previousSender = message.senderId;
        return (
          <Fragment key={message.seq}>
            {newDay && <div className="day-separator"><span>{day}</span></div>}
            <Bubble
              message={message}
              conversation={conversation}
              meId={state.meId}
              grouped={grouped}
              editing={editing === message.id}
              onStartEdit={() => setEditing(message.id)}
              onEndEdit={() => setEditing(null)}
              onEdit={onEdit}
              onDelete={onDelete}
            />
          </Fragment>
        );
      })}
      {pending.map((p) => (
        <div key={p.clientMessageId} className="bubble-row mine">
          <div className={`bubble mine${p.failed ? " failed" : " pending"}`}>
            <p className="body">{p.body}</p>
            <span className="meta">
              {p.failed ? (
                <button className="retry" onClick={() => onRetry(p)}><RotateCw size={12} aria-hidden /> Not sent · Retry</button>
              ) : (
                <><Clock size={12} aria-label="Sending" /></>
              )}
            </span>
          </div>
        </div>
      ))}
    </div>
  );
}

type BubbleProps = {
  message: Message;
  conversation: Conversation;
  meId: string;
  grouped: boolean;
  editing: boolean;
  onStartEdit: () => void;
  onEndEdit: () => void;
  onEdit: (message: Message, body: string) => Promise<void>;
  onDelete: (message: Message) => Promise<void>;
};

function Bubble({ message, conversation, meId, grouped, editing, onStartEdit, onEndEdit, onEdit, onDelete }: BubbleProps) {
  const mine = message.senderId === meId;
  const sender = conversation.members.find((m) => m.userId === message.senderId)?.displayName ?? "Former member";
  const deleted = message.deletedAt !== null;
  const [draft, setDraft] = useState(message.body);
  const [error, setError] = useState<string | null>(null);

  async function save(event: FormEvent) {
    event.preventDefault();
    try {
      await onEdit(message, draft);
      onEndEdit();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not save.");
    }
  }

  return (
    <div className={`bubble-row${mine ? " mine" : ""}${grouped ? " grouped" : ""}`} data-seq={message.seq}>
      {!mine && conversation.kind === "Group" && !grouped && <span className="sender">{sender}</span>}
      <div className={`bubble${mine ? " mine" : ""}${deleted ? " deleted" : ""}`}>
        {editing ? (
          <form className="edit-form" onSubmit={save}>
            <textarea value={draft} onChange={(e) => setDraft(e.target.value)} autoFocus rows={2} aria-label="Edited text" />
            {error && <span className="field-error">{error}</span>}
            <span className="edit-actions">
              <button type="button" className="icon-button small" onClick={onEndEdit} aria-label="Cancel editing"><X size={14} /></button>
              <button className="icon-button small" aria-label="Save"><Check size={14} /></button>
            </span>
          </form>
        ) : (
          <p className="body">{deleted ? "This message was deleted" : message.body}</p>
        )}
        <span className="meta">
          {message.editedAt && !deleted && <span>edited</span>}
          <time dateTime={message.createdAt}>{formatTime(message.createdAt)}</time>
          {mine && !deleted && <ReadTicks conversation={conversation} meId={meId} seq={message.seq} />}
        </span>
        {mine && !deleted && !editing && (
          <span className="bubble-actions">
            <button className="icon-button small" onClick={onStartEdit} aria-label="Edit message"><Pencil size={13} /></button>
            <button className="icon-button small" onClick={() => void onDelete(message)} aria-label="Delete message"><Trash2 size={13} /></button>
          </span>
        )}
      </div>
    </div>
  );
}

function ReadTicks({ conversation, meId, seq }: { conversation: Conversation; meId: string; seq: number }) {
  const { read, of } = readState(conversation, meId, seq);
  if (of > 0 && read === of) {
    return <CheckCheck size={14} className="ticks read" aria-label={of === 1 ? "Read" : "Read by everyone"} />;
  }

  return (
    <span className="ticks" aria-label={read > 0 ? `Read by ${read} of ${of}` : "Sent"}>
      <Check size={14} aria-hidden />
      {read > 0 && <span className="read-count">{read}/{of}</span>}
    </span>
  );
}

function Composer({ onSend, onTyping, disabled }: { onSend: (body: string) => void; onTyping: () => void; disabled: boolean }) {
  const [text, setText] = useState("");
  const lastTyping = useRef(0);
  const field = useRef<HTMLTextAreaElement>(null);

  useEffect(() => field.current?.focus(), []);

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const body = text.trim();
    if (!body) return;
    onSend(body);
    setText("");
  }

  function keyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) submit(event);
  }

  function change(value: string) {
    setText(value);
    const now = Date.now();
    if (value.trim() && now - lastTyping.current > 2500) {
      lastTyping.current = now;
      onTyping();
    }
  }

  return (
    <form className="composer" onSubmit={submit}>
      <textarea
        ref={field}
        rows={1}
        value={text}
        maxLength={4000}
        placeholder={disabled ? "Waiting for the connection…" : "Write a message"}
        onChange={(e) => change(e.target.value)}
        onKeyDown={keyDown}
        aria-label="Message"
      />
      <button className="button primary send" disabled={!text.trim()} aria-label="Send">
        <SendHorizontal size={18} aria-hidden />
      </button>
    </form>
  );
}
