import type { ReactNode } from 'react'
import { cx } from '../lib/cx'
import classes from './EmptyState.module.css'

type EmptyStateProps = {
  icon?: ReactNode
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  variant?: 'card' | 'plain'
  size?: 'md' | 'lg'
  className?: string
}

export function EmptyState({
  icon,
  title,
  description,
  actions,
  variant = 'card',
  size = 'md',
  className,
}: EmptyStateProps) {
  return (
    <div className={cx(classes.root, classes[variant], classes[size], className)} role="status">
      {icon && <div className={classes.icon}>{icon}</div>}
      <h3 className={classes.title}>{title}</h3>
      {description && <p className={classes.description}>{description}</p>}
      {actions && <div className={classes.actions}>{actions}</div>}
    </div>
  )
}
