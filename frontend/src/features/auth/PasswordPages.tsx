import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Alert, Button, PasswordInput, TextInput } from '@mantine/core'
import { useForm } from '@mantine/form'
import { Check, CircleAlert, CircleCheck, KeyRound } from 'lucide-react'
import { authApi } from '../../api/auth'
import { describeError } from '../../api/client'
import { cx } from '../../lib/cx'
import { isValidEmail, passwordRules, passwordSatisfiesRules } from './passwordRules'
import classes from './AuthForm.module.css'

export function ForgotPasswordPage() {
  const [sentTo, setSentTo] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const form = useForm({
    initialValues: { email: '' },
    validate: { email: (value) => (isValidEmail(value) ? null : 'Enter a valid email address') },
  })

  const submit = form.onSubmit(async (values) => {
    setSubmitting(true)
    setError(null)
    try {
      await authApi.forgotPassword(values.email.trim())
      setSentTo(values.email.trim())
    } catch (caught) {
      setError(describeError(caught))
    } finally {
      setSubmitting(false)
    }
  })

  if (sentTo) {
    return (
      <div className={classes.statusCard}>
        <div className={classes.statusIcon}>
          <KeyRound size={22} />
        </div>
        <h1 className={classes.title}>Check your inbox</h1>
        <p className={classes.subtitle}>
          If an account exists for <span className={classes.email}>{sentTo}</span>, we sent a link to reset the
          password.
        </p>
        <Button component={Link} to="/sign-in" variant="default" mt="md">
          Back to sign in
        </Button>
      </div>
    )
  }

  return (
    <>
      <div className={classes.header}>
        <h1 className={classes.title}>Reset your password</h1>
        <p className={classes.subtitle}>Enter your email and we'll send you a reset link.</p>
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
        {error && (
          <Alert color="red" variant="light" icon={<CircleAlert size={16} />}>
            {error}
          </Alert>
        )}
        <Button type="submit" size="md" fullWidth loading={submitting}>
          Send reset link
        </Button>
      </form>
      <p className={classes.footer}>
        <Link to="/sign-in" className={classes.link}>
          Back to sign in
        </Link>
      </p>
    </>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const userId = params.get('userId')
  const token = params.get('token')
  const [done, setDone] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const form = useForm({
    initialValues: { password: '', confirm: '' },
    validate: {
      password: (value) => (passwordSatisfiesRules(value) ? null : 'Password does not meet the requirements'),
      confirm: (value, values) => (value === values.password ? null : 'Passwords do not match'),
    },
  })

  if (!userId || !token) {
    return (
      <div className={classes.statusCard}>
        <div className={cx(classes.statusIcon, classes.statusIconBad)}>
          <CircleAlert size={22} />
        </div>
        <h1 className={classes.title}>This reset link is incomplete</h1>
        <p className={classes.subtitle}>Request a new link from the sign-in page.</p>
        <Button component={Link} to="/forgot-password" mt="md">
          Request a new link
        </Button>
      </div>
    )
  }

  if (done) {
    return (
      <div className={classes.statusCard}>
        <div className={cx(classes.statusIcon, classes.statusIconGood)}>
          <CircleCheck size={22} />
        </div>
        <h1 className={classes.title}>Password updated</h1>
        <p className={classes.subtitle}>You can sign in with your new password now.</p>
        <Button component={Link} to="/sign-in" size="md" mt="md" fullWidth>
          Sign in
        </Button>
      </div>
    )
  }

  const submit = form.onSubmit(async (values) => {
    setSubmitting(true)
    setError(null)
    try {
      await authApi.resetPassword({ userId, token, newPassword: values.password })
      setDone(true)
    } catch (caught) {
      setError(describeError(caught))
    } finally {
      setSubmitting(false)
    }
  })

  const password = form.values.password

  return (
    <>
      <div className={classes.header}>
        <h1 className={classes.title}>Choose a new password</h1>
        <p className={classes.subtitle}>Make it something you don't use anywhere else.</p>
      </div>
      <form className={classes.form} onSubmit={submit} noValidate>
        <div>
          <PasswordInput
            label="New password"
            autoComplete="new-password"
            autoFocus
            {...form.getInputProps('password')}
          />
          <ul className={classes.rules} style={{ marginTop: 10 }} aria-label="Password requirements">
            {passwordRules.map((rule) => {
              const met = rule.test(password)
              return (
                <li key={rule.id} className={cx(classes.rule, met && classes.ruleMet)}>
                  <Check size={12} strokeWidth={2.5} aria-hidden="true" style={{ opacity: met ? 1 : 0.35 }} />
                  <span>{rule.label}</span>
                </li>
              )
            })}
          </ul>
        </div>
        <PasswordInput
          label="Confirm new password"
          autoComplete="new-password"
          {...form.getInputProps('confirm')}
        />
        {error && (
          <Alert color="red" variant="light" icon={<CircleAlert size={16} />}>
            {error}
          </Alert>
        )}
        <Button type="submit" size="md" fullWidth loading={submitting}>
          Update password
        </Button>
      </form>
    </>
  )
}
