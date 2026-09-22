import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { cx } from '../lib/cx'
import classes from './PageHeader.module.css'

type PageHeaderProps = {
  eyebrow?: ReactNode
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  backTo?: { to: string; label: string }
  size?: 'lg' | 'md'
  /** Rendered under the header row, e.g. tabs. */
  children?: ReactNode
  className?: string
}

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
  backTo,
  size = 'lg',
  children,
  className,
}: PageHeaderProps) {
  return (
    <header className={cx(classes.root, size === 'md' && classes.md, className)}>
      {backTo && (
        <Link to={backTo.to} className={classes.back}>
          <ArrowLeft size={14} aria-hidden="true" />
          {backTo.label}
        </Link>
      )}
      <div className={classes.row}>
        <div className={classes.text}>
          {eyebrow && <div className={classes.eyebrow}>{eyebrow}</div>}
          <h1 className={classes.title}>{title}</h1>
          {description && <p className={classes.description}>{description}</p>}
        </div>
        {actions && <div className={classes.actions}>{actions}</div>}
      </div>
      {children}
    </header>
  )
}
