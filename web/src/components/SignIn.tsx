import { useEffect, useState, type FormEvent } from "react";
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
  const [demo, setDemo] = useState<{ displayName: string; email: string; password: string }[]>([]);

  // The public demo publishes two sign-ins; anywhere else the list is empty and nothing is shown.
  useEffect(() => {
    api.demo().then(setDemo, () => setDemo([]));
  }, []);

  async function signInAs(account: { email: string; password: string }) {
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.login(account.email, account.password));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
      setBusy(false);
    }
  }

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
        {mode === "login" && demo.length > 0 && (
          <div className="demo-sign-in">
            <p className="muted">
              This is a public demo. Sign in as one person here and as the other in a private window to watch messages,
              typing and read receipts arrive live. Elif and Can answer in their chats too.
            </p>
            {demo.map((account) => (
              <button key={account.email} type="button" className="button wide" disabled={busy} onClick={() => void signInAs(account)}>
                Sign in as {account.displayName}
              </button>
            ))}
          </div>
        )}
      </form>
    </main>
  );
}
