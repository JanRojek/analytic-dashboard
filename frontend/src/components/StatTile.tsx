import type { ReactNode } from 'react'
import { cx } from '../lib/cx'
import classes from './StatTile.module.css'

type StatTileProps = {
  label: ReactNode
  value: ReactNode
  hint?: ReactNode
  tone?: 'neutral' | 'good' | 'warn' | 'bad'
  /** Optional visual under the value, e.g. a sparkline or meter. */
  children?: ReactNode
  className?: string
}

export function StatTile({ label, value, hint, tone = 'neutral', children, className }: StatTileProps) {
  return (
    <div className={cx(classes.root, classes[tone], className)}>
      <div className={classes.label}>{label}</div>
      <div className={classes.value}>{value}</div>
      {hint && <div className={classes.hint}>{hint}</div>}
      {children && <div className={classes.extra}>{children}</div>}
    </div>
  )
}
