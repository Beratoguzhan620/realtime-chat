import { Check, LoaderCircle, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import type { Conversation, User } from "../types";
import { Avatar } from "./Avatar";

/** Finds people by name (or whole email) and starts a direct conversation, or a group with several. */
export function NewConversation({ onClose, onCreated }: { onClose: () => void; onCreated: (c: Conversation) => void }) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<User[]>([]);
  const [searching, setSearching] = useState(false);
  const [picked, setPicked] = useState<User[]>([]);
  const [title, setTitle] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    dialog.current?.showModal();
  }, []);

  useEffect(() => {
    const text = query.trim();
    if (text.length < 2) {
      setResults([]);
      return;
    }

    setSearching(true);
    const timer = setTimeout(() => {
      api.searchUsers(text)
        .then((users) => { setResults(users); setError(null); })
        .catch((e: Error) => setError(e.message))
        .finally(() => setSearching(false));
    }, 250);
    return () => clearTimeout(timer);
  }, [query]);

  const toggle = (user: User) =>
    setPicked((current) => (current.some((u) => u.id === user.id) ? current.filter((u) => u.id !== user.id) : [...current, user]));

  async function start() {
    setBusy(true);
    setError(null);
    try {
      const conversation = picked.length === 1
        ? await api.startDirect(picked[0].id)
        : await api.startGroup(title, picked.map((u) => u.id));
      onCreated(conversation);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not start the conversation.");
      setBusy(false);
    }
  }

  const isGroup = picked.length > 1;
  return (
    <dialog ref={dialog} className="dialog" onClose={onClose} aria-labelledby="new-title">
      <header className="dialog-header">
        <h2 id="new-title">New conversation</h2>
        <button className="icon-button" onClick={onClose} aria-label="Close"><X size={18} /></button>
      </header>

      <input
        className="input"
        autoFocus
        placeholder="Search people by name or email"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        aria-label="Search people"
      />

      {picked.length > 0 && (
        <div className="chips">
          {picked.map((u) => (
            <button key={u.id} className="chip" onClick={() => toggle(u)} aria-label={`Remove ${u.displayName}`}>
              {u.displayName} <X size={12} aria-hidden />
            </button>
          ))}
        </div>
      )}

      <ul className="people" aria-busy={searching}>
        {searching && <li className="muted padded"><LoaderCircle size={14} className="spin" aria-hidden /> Searching…</li>}
        {!searching && query.trim().length >= 2 && results.length === 0 && <li className="muted padded">Nobody found.</li>}
        {!searching && query.trim().length < 2 && picked.length === 0 && (
          <li className="muted padded">Type at least two letters of a name, or a whole email address.</li>
        )}
        {results.map((user) => {
          const selected = picked.some((u) => u.id === user.id);
          return (
            <li key={user.id}>
              <button className={`person${selected ? " selected" : ""}`} onClick={() => toggle(user)} aria-pressed={selected}>
                <Avatar name={user.displayName} size={32} />
                <span>{user.displayName}</span>
                {selected && <Check size={16} aria-hidden />}
              </button>
            </li>
          );
        })}
      </ul>

      {isGroup && (
        <label className="stacked">
          Group name
          <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} />
        </label>
      )}

      {error && <p className="form-error" role="alert">{error}</p>}

      <footer className="dialog-footer">
        <button className="button" onClick={onClose}>Cancel</button>
        <button className="button primary" disabled={busy || picked.length === 0 || (isGroup && !title.trim())} onClick={start}>
          {busy && <LoaderCircle size={16} className="spin" aria-hidden />}
          {isGroup ? "Create group" : "Start chat"}
        </button>
      </footer>
    </dialog>
  );
}
