import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Pencil } from 'lucide-react'
import { describeError } from '../api/client'
import { cx } from '../lib/cx'
import classes from './InlineEdit.module.css'

type InlineEditProps = {
  value: string
  onSave: (next: string) => Promise<void> | void
  validate?: (value: string) => string | null
  ariaLabel: string
  size?: 'lg' | 'md'
  className?: string
}

/** Click-to-edit text. Enter saves, Escape cancels, blur saves. */
export function InlineEdit({ value, onSave, validate, ariaLabel, size = 'lg', className }: InlineEditProps) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(value)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!editing) setDraft(value)
  }, [value, editing])

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus()
      inputRef.current?.select()
    }
  }, [editing])

  const cancel = () => {
    setEditing(false)
    setError(null)
    setDraft(value)
  }

  const commit = async () => {
    const next = draft.trim()
    if (next === value.trim() || next.length === 0) {
      cancel()
      return
    }
    const validation = validate?.(next) ?? null
    if (validation) {
      setError(validation)
      return
    }
    try {
      setSaving(true)
      await onSave(next)
      setEditing(false)
      setError(null)
    } catch (caught) {
      setError(describeError(caught))
      inputRef.current?.focus()
    } finally {
      setSaving(false)
    }
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void commit()
    } else if (event.key === 'Escape') {
      event.preventDefault()
      cancel()
    }
  }

  if (editing) {
    return (
      <div className={cx(classes.root, classes[size], className)}>
        <input
          ref={inputRef}
          className={cx(classes.input, error && classes.inputError)}
          value={draft}
          onChange={(event) => {
            setDraft(event.currentTarget.value)
            setError(null)
          }}
          onKeyDown={onKeyDown}
          onBlur={() => void commit()}
          disabled={saving}
          aria-label={ariaLabel}
          aria-invalid={error ? true : undefined}
          maxLength={100}
        />
        {error && (
          <div className={classes.error} role="alert">
            {error}
          </div>
        )}
      </div>
    )
  }

  return (
    <button
      type="button"
      className={cx(classes.root, classes.button, classes[size], className)}
      onClick={() => setEditing(true)}
      aria-label={`${ariaLabel}: ${value}. Click to rename`}
    >
      <span className={classes.text}>{value}</span>
      <Pencil size={size === 'lg' ? 15 : 13} className={classes.pencil} aria-hidden="true" />
    </button>
  )
}
