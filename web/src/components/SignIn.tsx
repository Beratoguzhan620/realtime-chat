import { useState, type FormEvent } from "react";
import { LoaderCircle, MessagesSquare } from "lucide-react";
import { api, ApiError } from "../api";
import type { Session } from "../types";

export function SignIn({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string[]>>({});

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setFields({});
    try {
      onSignedIn(mode === "login" ? await api.login(email, password) : await api.register(email, displayName, password));
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length > 0) setFields(e.fields);
      else setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  const fieldError = (name: string) =>
    fields[name] ? <span className="field-error" role="alert">{fields[name][0]}</span> : null;

  return (
    <main className="auth">
      <form className="auth-card" onSubmit={submit} aria-label={mode === "login" ? "Sign in" : "Create an account"}>
        <div className="brand">
          <span className="brand-mark"><MessagesSquare size={18} aria-hidden /></span>
          <span>Realtime Chat</span>
        </div>
        <h1>{mode === "login" ? "Sign in" : "Create an account"}</h1>
        <p className="muted">{mode === "login" ? "Welcome back." : "It takes a few seconds."}</p>

        <label>
          Email
          <input type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          {fieldError("email")}
        </label>
        {mode === "register" && (
          <label>
            Display name
            <input value={displayName} autoComplete="name" onChange={(e) => setDisplayName(e.target.value)} required />
            {fieldError("displayName")}
          </label>
        )}
        <label>
          Password
          <input
            type="password"
            autoComplete={mode === "login" ? "current-password" : "new-password"}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
          {fieldError("password")}
        </label>

        {error && <p className="form-error" role="alert">{error}</p>}

        <button className="button primary wide" disabled={busy}>
          {busy && <LoaderCircle size={16} className="spin" aria-hidden />}
          {mode === "login" ? "Sign in" : "Create account"}
        </button>
        <button
          type="button"
          className="link"
          onClick={() => {
            setMode(mode === "login" ? "register" : "login");
            setError(null);
            setFields({});
          }}
        >
          {mode === "login" ? "New here? Create an account" : "Already have an account? Sign in"}
        </button>
      </form>
    </main>
  );
}
