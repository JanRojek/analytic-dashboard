import type { ReactNode } from 'react'
import { cx } from '../lib/cx'
import classes from './StatusBadge.module.css'

export type StatusTone = 'good' | 'warn' | 'bad' | 'info' | 'neutral' | 'accent'

type StatusBadgeProps = {
  tone: StatusTone
  children: ReactNode
  dot?: boolean
  /** Gentle pulse for in-progress states. */
  pulse?: boolean
  className?: string
}

export function StatusBadge({ tone, children, dot = true, pulse, className }: StatusBadgeProps) {
  return (
    <span className={cx(classes.root, classes[tone], className)}>
      {dot && <span className={cx(classes.dot, pulse && classes.pulse)} aria-hidden="true" />}
      {children}
    </span>
  )
}
