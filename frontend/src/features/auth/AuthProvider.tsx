import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { authApi } from '../../api/auth'
import { ApiError, onUnauthorized } from '../../api/client'
import { queryKeys } from '../../api/queryKeys'
import type { CurrentUser } from '../../api/types'

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous' | 'error'

type AuthContextValue = {
  user: CurrentUser | null
  status: AuthStatus
  error: unknown
  /** Re-reads the session from the server (after sign-in or registration). */
  refresh: () => Promise<CurrentUser | null>
  signOut: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

async function fetchCurrentUser(signal: AbortSignal): Promise<CurrentUser | null> {
  try {
    return await authApi.me(signal)
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: queryKeys.me,
    queryFn: ({ signal }) => fetchCurrentUser(signal),
    staleTime: 5 * 60_000,
    retry: false,
  })

  useEffect(
    () =>
      onUnauthorized(() => {
        // Session expired mid-flight: drop the cached user so guards redirect.
        queryClient.setQueryData(queryKeys.me, null)
      }),
    [queryClient],
  )

  const refresh = useCallback(async () => {
    const result = await queryClient.fetchQuery({
      queryKey: queryKeys.me,
      queryFn: ({ signal }) => fetchCurrentUser(signal),
      staleTime: 0,
    })
    return result
  }, [queryClient])

  const signOut = useCallback(async () => {
    try {
      await authApi.logout()
    } finally {
      queryClient.setQueryData(queryKeys.me, null)
      // Forget everything that belonged to the signed-out user.
      queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'auth' })
    }
  }, [queryClient])

  const value = useMemo<AuthContextValue>(() => {
    let status: AuthStatus
    if (query.isPending) status = 'loading'
    else if (query.isError) status = 'error'
    else status = query.data ? 'authenticated' : 'anonymous'
    return { user: query.data ?? null, status, error: query.error, refresh, signOut }
  }, [query.data, query.error, query.isError, query.isPending, refresh, signOut])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used within AuthProvider')
  return context
}
