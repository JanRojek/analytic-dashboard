import { useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { Alert, Button, PasswordInput, TextInput } from '@mantine/core'
import { useForm } from '@mantine/form'
import { Check, CircleAlert } from 'lucide-react'
import { authApi } from '../../api/auth'
import { ApiError, describeError } from '../../api/client'
import { cx } from '../../lib/cx'
import { useAuth } from './AuthProvider'
import { isValidEmail, passwordRules, passwordSatisfiesRules } from './passwordRules'
import { rememberRegistrationEmail } from './registrationSession'
import classes from './AuthForm.module.css'

export function RegisterPage() {
  const { status } = useAuth()
  const navigate = useNavigate()
  const [submitting, setSubmitting] = useState(false)
  const [generalError, setGeneralError] = useState<string | null>(null)

  const form = useForm({
    initialValues: { displayName: '', email: '', password: '' },
    validate: {
      displayName: (value) => {
        const trimmed = value.trim()
        if (trimmed.length === 0) return 'Tell us what to call you'
        if (trimmed.length > 100) return 'Use at most 100 characters'
        return null
      },
      email: (value) => (isValidEmail(value) ? null : 'Enter a valid email address'),
      password: (value) => (passwordSatisfiesRules(value) ? null : 'Password does not meet the requirements'),
    },
  })

  if (status === 'authenticated') return <Navigate to="/projects" replace />

  const submit = form.onSubmit(async (values) => {
    setSubmitting(true)
    setGeneralError(null)
    const email = values.email.trim()
    try {
      await authApi.register({
        email,
        displayName: values.displayName.trim(),
        password: values.password,
      })
      rememberRegistrationEmail(email)
      navigate('/register/confirm', { replace: true })
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        form.setFieldError('email', 'An account with this email already exists.')
      } else if (error instanceof ApiError && error.status === 400) {
        const message = error.detail ?? error.title
        if (error.title.toLowerCase().includes('password')) form.setFieldError('password', message)
        else if (error.title.toLowerCase().includes('email')) form.setFieldError('email', message)
        else if (error.title.toLowerCase().includes('display')) form.setFieldError('displayName', message)
        else setGeneralError(message)
      } else {
        setGeneralError(describeError(error))
      }
    } finally {
      setSubmitting(false)
    }
  })

  const password = form.values.password

  return (
    <>
      <div className={classes.header}>
        <h1 className={classes.title}>Create your account</h1>
        <p className={classes.subtitle}>Start with a project, bring in a CSV, and build your first dashboard.</p>
      </div>

      <form className={classes.form} onSubmit={submit} noValidate>
        <TextInput
          label="Name"
          autoComplete="name"
          placeholder="How should we address you?"
          autoFocus
          {...form.getInputProps('displayName')}
        />
        <TextInput
          label="Email"
          type="email"
          autoComplete="email"
          placeholder="you@company.com"
          {...form.getInputProps('email')}
        />
        <div>
          <PasswordInput
            label="Password"
            autoComplete="new-password"
            placeholder="Choose a strong password"
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

        {generalError && (
          <Alert color="red" variant="light" icon={<CircleAlert size={16} />}>
            {generalError}
          </Alert>
        )}

        <Button type="submit" size="md" fullWidth loading={submitting}>
          Create account
        </Button>
      </form>

      <p className={classes.footer}>
        Already have an account?{' '}
        <Link to="/sign-in" className={classes.link}>
          Sign in
        </Link>
      </p>
    </>
  )
}
