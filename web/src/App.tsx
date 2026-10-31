import { useCallback, useState } from "react";
import { setToken } from "./api";
import { ChatApp } from "./components/ChatApp";
import { SignIn } from "./components/SignIn";
import { loadSession, saveSession } from "./session";
import type { Session } from "./types";

export function App() {
  const [session, setSession] = useState<Session | null>(() => {
    const saved = loadSession();
    setToken(saved?.accessToken ?? null, () => {});
    return saved;
  });

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
  }, []);

  const signedIn = useCallback(
    (next: Session) => {
      saveSession(next);
      setToken(next.accessToken, signOut);
      setSession(next);
    },
    [signOut],
  );

  if (session) setToken(session.accessToken, signOut);
  return session ? <ChatApp key={session.user.id} session={session} onSignOut={signOut} /> : <SignIn onSignedIn={signedIn} />;
}
