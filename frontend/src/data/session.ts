import { createContext, useContext } from "react";
import type { DataAdapter, User } from "./types";

export type SessionMode = "demo" | "api";
export type Session = { mode: SessionMode; user: User };
export type SessionContextValue = {
  session: Session | null;
  mode: SessionMode;
  adapter: DataAdapter;
  loading: boolean;
  restorationError: Error | null;
  retrySession: () => void;
  clearSession: () => void;
  startDemo: () => void;
  signIn: (user: User) => void;
  signOut: () => Promise<void>;
};
export const SessionContext = createContext<SessionContextValue | null>(null);
export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error("SessionProvider is missing.");
  return value;
}
