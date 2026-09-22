import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert, Button, Checkbox, PasswordInput, TextInput } from '@mantine/core'
import { useForm } from '@mantine/form'
import { notifications } from '@mantine/notifications'
import { CircleAlert, MailCheck } from 'lucide-react'
import { authApi } from '../../api/auth'
import { ApiError, describeError } from '../../api/client'
import { brand } from '../../brand/brand'
import { useAuth } from './AuthProvider'
import { isValidEmail } from './passwordRules'
import classes from './AuthForm.module.css'

type LocationState = { from?: string } | null

type Problem = { kind: 'credentials' | 'unconfirmed' | 'other'; message: string }

export function SignInPage() {
  const { status, refresh } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as LocationState)?.from ?? '/projects'

  const [submitting, setSubmitting] = useState(false)
  const [resending, setResending] = useState(false)
  const [problem, setProblem] = useState<Problem | null>(null)

  const form = useForm({
    initialValues: { email: '', password: '', rememberMe: true },
    validate: {
      email: (value) => (isValidEmail(value) ? null : 'Enter a valid email address'),
      password: (value) => (value.length > 0 ? null : 'Enter your password'),
    },
  })

  if (status === 'authenticated') return <Navigate to={from} replace />

  const submit = form.onSubmit(async (values) => {
    setSubmitting(true)
    setProblem(null)
    try {
      await authApi.login({
        email: values.email.trim(),
        password: values.password,
        rememberMe: values.rememberMe,
      })
      await refresh()
      navigate(from, { replace: true })
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setProblem({ kind: 'credentials', message: 'Incorrect email or password.' })
      } else if (error instanceof ApiError && error.status === 409) {
        setProblem({
          kind: 'unconfirmed',
          message: 'This email address has not been confirmed yet. Open the link we sent you, or request a new one.',
        })
      } else {
        setProblem({ kind: 'other', message: describeError(error) })
      }
    } finally {
      setSubmitting(false)
    }
  })

  const resend = async () => {
    setResending(true)
    try {
      await authApi.resendConfirmation(form.values.email.trim())
      notifications.show({ message: 'Confirmation email sent. Check your inbox.' })
    } catch (error) {
      notifications.show({ color: 'red', message: describeError(error) })
    } finally {
      setResending(false)
    }
  }

  return (
    <>
      <div className={classes.header}>
        <h1 className={classes.title}>Welcome back</h1>
        <p className={classes.subtitle}>Sign in to continue to your projects.</p>
      </div>

      <form className={classes.form} onSubmit={submit} noValidate>
        <TextInput
          label="Email"
          type="email"
          autoComplete="email"
          placeholder="you@company.com"
          autoFocus
          {...form.getInputProps('email')}
        />
        <PasswordInput
          label="Password"
          autoComplete="current-password"
          placeholder="Your password"
          {...form.getInputProps('password')}
        />
        <div className={classes.rowBetween}>
          <Checkbox
            label="Keep me signed in"
            size="sm"
            {...form.getInputProps('rememberMe', { type: 'checkbox' })}
          />
          <Link to="/forgot-password" className={classes.link}>
            Forgot password?
          </Link>
        </div>

        {problem && (
          <Alert
            color={problem.kind === 'unconfirmed' ? 'yellow' : 'red'}
            variant="light"
            icon={problem.kind === 'unconfirmed' ? <MailCheck size={16} /> : <CircleAlert size={16} />}
            title={problem.kind === 'unconfirmed' ? 'Confirm your email' : undefined}
          >
            {problem.message}
            {problem.kind === 'unconfirmed' && (
              <div style={{ marginTop: 10 }}>
                <Button size="xs" variant="default" onClick={() => void resend()} loading={resending}>
                  Resend confirmation email
                </Button>
              </div>
            )}
          </Alert>
        )}

        <Button type="submit" size="md" fullWidth loading={submitting}>
          Sign in
        </Button>
      </form>

      <p className={classes.footer}>
        New to {brand.name}?{' '}
        <Link to="/register" className={classes.link}>
          Create an account
        </Link>
      </p>
    </>
  )
}
