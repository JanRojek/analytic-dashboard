import { Button } from '@mantine/core'
import { CircleAlert, RefreshCw } from 'lucide-react'
import { describeError } from '../api/client'
import { cx } from '../lib/cx'
import classes from './ErrorState.module.css'

type ErrorStateProps = {
  title?: string
  error?: unknown
  onRetry?: () => void
  compact?: boolean
  className?: string
}

export function ErrorState({
  title = "Couldn't load this",
  error,
  onRetry,
  compact,
  className,
}: ErrorStateProps) {
  return (
    <div className={cx(classes.root, compact && classes.compact, className)} role="alert">
      <CircleAlert size={compact ? 16 : 20} className={classes.icon} aria-hidden="true" />
      <div className={classes.text}>
        <div className={classes.title}>{title}</div>
        {error !== undefined && <div className={classes.message}>{describeError(error)}</div>}
      </div>
      {onRetry && (
        <Button
          variant="default"
          size="xs"
          leftSection={<RefreshCw size={13} />}
          onClick={onRetry}
          className={classes.retry}
        >
          Retry
        </Button>
      )}
    </div>
  )
}
