import { Calendar, Hash, Type } from 'lucide-react'
import type { ColumnType } from '../api/types'
import { cx } from '../lib/cx'
import classes from './TypeIcon.module.css'

export const columnTypeLabel: Record<ColumnType, string> = {
  number: 'Number',
  text: 'Text',
  date: 'Date',
}

type TypeIconProps = {
  type: ColumnType
  size?: number
  className?: string
}

/** Compact column-type glyph used in tables, field lists and headers. */
export function TypeIcon({ type, size = 13, className }: TypeIconProps) {
  const Icon = type === 'number' ? Hash : type === 'date' ? Calendar : Type
  return (
    <span
      className={cx(classes.root, classes[type], className)}
      title={columnTypeLabel[type]}
      aria-label={columnTypeLabel[type]}
      role="img"
    >
      <Icon size={size} strokeWidth={2} />
    </span>
  )
}
