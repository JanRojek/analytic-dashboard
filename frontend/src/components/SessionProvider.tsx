import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getAdapter } from "../data/adapter";
import { ApiError, authApi, subscribeToSessionExpiry } from "../data/api";
import { SessionContext, type Session } from "../data/session";
import type { User } from "../data/types";
import { demoUser } from "../mocks/seed";

const key = "aperture.session.v1";
function readSession(): Session | null {
  try {
    const value = JSON.parse(
      localStorage.getItem(key) || "null",
    ) as Session | null;
    return value &&
      (value.mode === "demo" || value.mode === "api") &&
      value.user?.id
      ? value
      : null;
  } catch {
    return null;
  }
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(readSession);
  const [loading, setLoading] = useState(() => readSession()?.mode === "api");
  const [restorationError, setRestorationError] = useState<Error | null>(null);
  const [restoreAttempt, setRestoreAttempt] = useState(0);
  const queryClient = useQueryClient();
  const clearSession = useCallback(() => {
    try {
      localStorage.removeItem(key);
    } catch {
      /* A blocked browser store does not prevent signing out of this tab. */
    }
    queryClient.clear();
    setSession(null);
    setRestorationError(null);
    setLoading(false);
  }, [queryClient]);
  useEffect(
    () =>
      subscribeToSessionExpiry(() => {
        if (readSession()?.mode === "api") clearSession();
      }),
    [clearSession],
  );
  useEffect(() => {
    if (readSession()?.mode !== "api") return;
    let active = true;
    authApi
      .me()
      .then((user) => {
        if (active) {
          setSession({ mode: "api", user });
          setRestorationError(null);
        }
      })
      .catch((error: unknown) => {
        if (!active) return;
        if (error instanceof ApiError && error.status === 401) clearSession();
        else
          setRestorationError(
            error instanceof Error
              ? error
              : new Error("Unable to restore your session. Please try again."),
          );
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [clearSession, restoreAttempt]);
  function enter(value: Session) {
    localStorage.setItem(key, JSON.stringify(value));
    queryClient.clear();
    setRestorationError(null);
    setLoading(false);
    setSession(value);
  }
  async function signOut() {
    if (session?.mode === "api") {
      try {
        await authApi.logout();
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 401)) throw error;
      }
    }
    clearSession();
  }
  return (
    <SessionContext.Provider
      value={{
        session,
        mode: session?.mode || "api",
        adapter: getAdapter(session?.mode || "api"),
        loading,
        restorationError,
        clearSession,
        retrySession: () => {
          setLoading(true);
          setRestoreAttempt((value) => value + 1);
        },
        startDemo: () => enter({ mode: "demo", user: demoUser }),
        signIn: (user: User) => enter({ mode: "api", user }),
        signOut,
      }}
    >
      {children}
    </SessionContext.Provider>
  );
}
