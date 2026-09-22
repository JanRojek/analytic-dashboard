import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { Button } from '@mantine/core'
import { FullPageLoader } from '../../layout/FullPageLoader'
import { ErrorState } from '../../components/ErrorState'
import { useAuth } from './AuthProvider'
import classes from './RequireAuth.module.css'

export function RequireAuth({ children }: { children: ReactNode }) {
  const { status, error, refresh } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <FullPageLoader />

  if (status === 'error') {
    return (
      <div className={classes.errorPage}>
        <div className={classes.errorCard}>
          <ErrorState
            title="We can't reach the server right now"
            error={error}
            onRetry={() => void refresh()}
          />
          <Button variant="subtle" color="gray" onClick={() => window.location.reload()}>
            Reload the page
          </Button>
        </div>
      </div>
    )
  }

  if (status === 'anonymous') {
    const from = `${location.pathname}${location.search}`
    return <Navigate to="/sign-in" replace state={{ from }} />
  }

  return <>{children}</>
}
