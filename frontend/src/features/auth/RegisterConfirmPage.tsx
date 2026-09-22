import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Loader } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { CircleAlert, MailCheck } from 'lucide-react'
import { authApi } from '../../api/auth'
import { ApiError, describeError } from '../../api/client'
import { queryKeys } from '../../api/queryKeys'
import { cx } from '../../lib/cx'
import { useAuth } from './AuthProvider'
import { clearRegistrationEmail, readRegistrationEmail } from './registrationSession'
import classes from './AuthForm.module.css'

/**
 * Shown right after registration. Polls the registration status and signs the
 * user in automatically once the confirmation link has been opened.
 */
export function RegisterConfirmPage() {
  const navigate = useNavigate()
  const { refresh } = useAuth()
  const [email] = useState(() => readRegistrationEmail())
  const [resending, setResending] = useState(false)
  const [completionError, setCompletionError] = useState<string | null>(null)
  const completing = useRef(false)

  const statusQuery = useQuery({
    queryKey: queryKeys.registrationStatus,
    queryFn: ({ signal }) => authApi.registrationStatus(signal),
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.status === 'Confirmed' || query.state.error ? false : 3000,
  })

  const confirmed = statusQuery.data?.status === 'Confirmed'
  const sessionExpired = statusQuery.error instanceof ApiError && statusQuery.error.status === 401

  useEffect(() => {
    if (!confirmed || completing.current) return
    completing.current = true
    void (async () => {
      try {
        await authApi.completeRegistration()
        clearRegistrationEmail()
        await refresh()
        navigate('/projects', { replace: true })
      } catch (error) {
        completing.current = false
        setCompletionError(describeError(error))
      }
    })()
  }, [confirmed, navigate, refresh])

  const resend = async () => {
    if (!email) return
    setResending(true)
    try {
      await authApi.resendConfirmation(email)
      notifications.show({ message: 'Confirmation email sent again.' })
    } catch (error) {
      notifications.show({ color: 'red', message: describeError(error) })
    } finally {
      setResending(false)
    }
  }

  if (sessionExpired) {
    return (
      <div className={classes.statusCard}>
        <div className={cx(classes.statusIcon, classes.statusIconBad)}>
          <CircleAlert size={22} />
        </div>
        <h1 className={classes.title}>This sign-up session has expired</h1>
        <p className={classes.subtitle}>
          If you already confirmed your email, you can sign in right away. Otherwise, register again to receive a new link.
        </p>
        <Button component={Link} to="/sign-in" size="md" mt="md" fullWidth>
          Go to sign in
        </Button>
        <Button component={Link} to="/register" variant="subtle" color="gray">
          Register again
        </Button>
      </div>
    )
  }

  return (
    <div className={classes.statusCard}>
      <div className={classes.statusIcon}>
        <MailCheck size={22} />
      </div>
      <h1 className={classes.title}>Check your inbox</h1>
      <p className={classes.subtitle}>
        We sent a confirmation link{email ? ' to ' : '.'}
        {email && <span className={classes.email}>{email}</span>}
        {email && '.'} Open it to activate your account — this page will continue on its own.
      </p>

      {confirmed ? (
        <span className={classes.waiting}>
          <Loader size={14} color="gray" /> Email confirmed, signing you in…
        </span>
      ) : (
        <span className={classes.waiting}>
          <Loader size={14} color="gray" type="dots" /> Waiting for confirmation
        </span>
      )}

      {completionError && (
        <Alert color="red" variant="light" icon={<CircleAlert size={16} />} w="100%" mt="sm">
          {completionError}
        </Alert>
      )}

      <div style={{ display: 'flex', gap: 8, marginTop: 16, flexWrap: 'wrap', justifyContent: 'center' }}>
        <Button variant="default" onClick={() => void resend()} loading={resending} disabled={!email}>
          Resend email
        </Button>
        <Button component={Link} to="/register" variant="subtle" color="gray">
          Use a different email
        </Button>
      </div>

      <p className={classes.footer} style={{ marginTop: 16 }}>
        Already confirmed?{' '}
        <Link to="/sign-in" className={classes.link}>
          Sign in
        </Link>
      </p>
    </div>
  )
}
