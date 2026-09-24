import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getAdapter } from "../data/adapter";
import { ApiError, authApi, subscribeToSessionExpiry } from "../data/api";
import { SessionContext, type Session } from "../data/session";
import type { User } from "../data/types";
import { demoUser } from "../mocks/seed";

const modeKey = "aperture.mode.v1";
const legacySessionKey = "aperture.session.v1";

function readStoredMode(): "demo" | null {
  try {
    if (localStorage.getItem(modeKey) === "demo") {
      return "demo";
    }

    // Temporary compatibility with sessions saved by the previous version.
    const legacy = JSON.parse(
        localStorage.getItem(legacySessionKey) || "null",
    ) as Session | null;

    return legacy?.mode === "demo" ? "demo" : null;
  } catch {
    return null;
  }
}

function clearStoredMode() {
  try {
    localStorage.removeItem(modeKey);
    localStorage.removeItem(legacySessionKey);
  } catch {
    // Browser storage is optional for API sessions.
  }
}

function storeDemoMode() {
  try {
    localStorage.setItem(modeKey, "demo");
    localStorage.removeItem(legacySessionKey);
  } catch {
    // Demo still works for the current tab if storage is unavailable.
  }
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();

  const [session, setSession] = useState<Session | null>(() =>
      readStoredMode() === "demo"
          ? { mode: "demo", user: demoUser }
          : null,
  );

  const [loading, setLoading] = useState(
      () => readStoredMode() !== "demo",
  );

  const [restorationError, setRestorationError] =
      useState<Error | null>(null);

  const [restoreAttempt, setRestoreAttempt] = useState(0);

  const clearSession = useCallback(() => {
    clearStoredMode();
    queryClient.clear();
    setSession(null);
    setRestorationError(null);
    setLoading(false);
  }, [queryClient]);

  useEffect(
      () =>
          subscribeToSessionExpiry(() => {
            if (session?.mode === "api") {
              clearSession();
            }
          }),
      [clearSession, session?.mode],
  );

  useEffect(() => {
    if (readStoredMode() === "demo") {
      setLoading(false);
      return;
    }

    let active = true;

    authApi
        .me()
        .then((user) => {
          if (!active) return;

          setSession({
            mode: "api",
            user,
          });

          setRestorationError(null);
        })
        .catch((error: unknown) => {
          if (!active) return;

          if (error instanceof ApiError && error.status === 401) {
            clearSession();
            return;
          }

          setRestorationError(
              error instanceof Error
                  ? error
                  : new Error(
                      "Unable to restore your session. Please try again.",
                  ),
          );
        })
        .finally(() => {
          if (active) {
            setLoading(false);
          }
        });

    return () => {
      active = false;
    };
  }, [clearSession, restoreAttempt]);

  function enterDemo() {
    storeDemoMode();
    queryClient.clear();
    setRestorationError(null);
    setLoading(false);

    setSession({
      mode: "demo",
      user: demoUser,
    });
  }

  function enterApi(user: User) {
    clearStoredMode();
    queryClient.clear();
    setRestorationError(null);
    setLoading(false);

    setSession({
      mode: "api",
      user,
    });
  }

  async function signOut() {
    if (session?.mode === "api") {
      try {
        await authApi.logout();
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 401)) {
          throw error;
        }
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

            startDemo: enterDemo,
            signIn: enterApi,
            signOut,
          }}
      >
        {children}
      </SessionContext.Provider>
  );
}
