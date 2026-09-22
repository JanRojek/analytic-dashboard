const numberFormat = new Intl.NumberFormat('en-US')
const compactFormat = new Intl.NumberFormat('en-US', {
  notation: 'compact',
  maximumFractionDigits: 1,
})
const dateFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})
const relativeFormat = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

export function formatNumber(value: number, maximumFractionDigits = 2): string {
  if (!Number.isFinite(value)) return '—'
  if (Number.isInteger(value)) return numberFormat.format(value)
  return new Intl.NumberFormat('en-US', { maximumFractionDigits }).format(value)
}

/** 1,284 · 12.9K · 4.2M — for stat tiles and axis ticks. */
export function formatCompact(value: number): string {
  if (!Number.isFinite(value)) return '—'
  if (Math.abs(value) < 10_000) return formatNumber(value, Math.abs(value) < 10 ? 2 : 1)
  return compactFormat.format(value)
}

export function formatPercent(ratio: number, digits = 0): string {
  if (!Number.isFinite(ratio)) return '—'
  return `${(ratio * 100).toFixed(digits)}%`
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[unit]}`
}

export function formatDate(iso: string | Date): string {
  const date = typeof iso === 'string' ? new Date(iso) : iso
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}

export function formatDateTime(iso: string | Date): string {
  const date = typeof iso === 'string' ? new Date(iso) : iso
  return Number.isNaN(date.getTime()) ? '—' : dateTimeFormat.format(date)
}

export function formatRelative(iso: string | Date, now = Date.now()): string {
  const date = typeof iso === 'string' ? new Date(iso) : iso
  const diffSeconds = Math.round((date.getTime() - now) / 1000)
  const abs = Math.abs(diffSeconds)
  if (abs < 45) return 'just now'
  if (abs < 3600) return relativeFormat.format(Math.round(diffSeconds / 60), 'minute')
  if (abs < 86_400) return relativeFormat.format(Math.round(diffSeconds / 3600), 'hour')
  if (abs < 86_400 * 30) return relativeFormat.format(Math.round(diffSeconds / 86_400), 'day')
  return formatDate(date)
}

export function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${formatNumber(count)} ${count === 1 ? singular : plural}`
}

/** Removes a trailing file extension: "sales-2026.csv" → "sales-2026". */
export function stripExtension(fileName: string): string {
  const index = fileName.lastIndexOf('.')
  return index > 0 ? fileName.slice(0, index) : fileName
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}
