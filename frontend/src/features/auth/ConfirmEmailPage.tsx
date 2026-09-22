import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Button, Loader } from '@mantine/core'
import { CircleAlert, CircleCheck } from 'lucide-react'
import { authApi } from '../../api/auth'
import { describeError } from '../../api/client'
import { cx } from '../../lib/cx'
import { useAuth } from './AuthProvider'
import { clearRegistrationEmail } from './registrationSession'
import classes from './AuthForm.module.css'

type State = { kind: 'working' } | { kind: 'done' } | { kind: 'failed'; message: string }

/** Landing page for the confirmation link sent by email. */
export function ConfirmEmailPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { refresh } = useAuth()
  const userId = params.get('userId')
  const token = params.get('token')
  const started = useRef(false)
  const [state, setState] = useState<State>(() =>
    userId && token ? { kind: 'working' } : { kind: 'failed', message: 'This confirmation link is incomplete.' },
  )
  const [continuing, setContinuing] = useState(false)

  useEffect(() => {
    if (!userId || !token || started.current) return
    started.current = true
    authApi
      .confirmEmail({ userId, token })
      .then(() => setState({ kind: 'done' }))
      .catch((error: unknown) => setState({ kind: 'failed', message: describeError(error) }))
  }, [userId, token])

  const continueToApp = async () => {
    setContinuing(true)
    try {
      // Works when the registration session cookie is present in this browser.
      await authApi.completeRegistration()
      clearRegistrationEmail()
      await refresh()
      navigate('/projects', { replace: true })
    } catch {
      navigate('/sign-in', { replace: true })
    } finally {
      setContinuing(false)
    }
  }

  if (state.kind === 'working') {
    return (
      <div className={classes.statusCard}>
        <div className={classes.statusIcon}>
          <Loader size={22} color="gray" />
        </div>
        <h1 className={classes.title}>Confirming your email…</h1>
      </div>
    )
  }

  if (state.kind === 'failed') {
    return (
      <div className={classes.statusCard}>
        <div className={cx(classes.statusIcon, classes.statusIconBad)}>
          <CircleAlert size={22} />
        </div>
        <h1 className={classes.title}>We couldn't confirm this link</h1>
        <p className={classes.subtitle}>{state.message}</p>
        <Button component={Link} to="/sign-in" size="md" mt="md" fullWidth>
          Go to sign in
        </Button>
        <p className={classes.footer}>
          You can request a new link from the sign-in page if your email isn't confirmed yet.
        </p>
      </div>
    )
  }

  return (
    <div className={classes.statusCard}>
      <div className={cx(classes.statusIcon, classes.statusIconGood)}>
        <CircleCheck size={22} />
      </div>
      <h1 className={classes.title}>Your email is confirmed</h1>
      <p className={classes.subtitle}>Thanks — your account is ready to use.</p>
      <Button size="md" mt="md" fullWidth onClick={() => void continueToApp()} loading={continuing}>
        Continue
      </Button>
    </div>
  )
}
