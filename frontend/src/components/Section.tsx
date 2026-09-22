import type { ReactNode } from 'react'
import { cx } from '../lib/cx'
import classes from './Section.module.css'

type SectionProps = {
  title?: ReactNode
  description?: ReactNode
  action?: ReactNode
  children: ReactNode
  padding?: 'none' | 'md' | 'lg'
  className?: string
  id?: string
}

/** A bordered content block: the main structural unit of most pages. */
export function Section({
  title,
  description,
  action,
  children,
  padding = 'lg',
  className,
  id,
}: SectionProps) {
  return (
    <section id={id} className={cx(classes.root, className)}>
      {(title || action) && (
        <header className={classes.header}>
          <div className={classes.heading}>
            {title && <h2 className={classes.title}>{title}</h2>}
            {description && <p className={classes.description}>{description}</p>}
          </div>
          {action && <div className={classes.action}>{action}</div>}
        </header>
      )}
      <div className={cx(classes.body, classes[`pad_${padding}`])}>{children}</div>
    </section>
  )
}
