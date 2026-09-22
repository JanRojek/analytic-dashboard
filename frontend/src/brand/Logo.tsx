import { brand } from './brand'
import classes from './Logo.module.css'

type LogoMarkProps = {
  size?: number
  tone?: 'ink' | 'inverse'
  className?: string
}

/**
 * The mark is a cairn: three stacked stones that also read as a sorted bar
 * chart. The top stone carries the accent.
 */
export function LogoMark({ size = 24, tone = 'ink', className }: LogoMarkProps) {
  const stone = tone === 'ink' ? '#1c1917' : '#ffffff'
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
      className={className}
    >
      <rect x="3" y="17" width="18" height="4" rx="2" fill={stone} />
      <rect x="6" y="10.5" width="12" height="4" rx="2" fill={stone} />
      <rect x="9" y="4" width="6" height="4" rx="2" fill="#14b8a6" />
    </svg>
  )
}

type LogoProps = {
  size?: 'sm' | 'md' | 'lg'
  tone?: 'ink' | 'inverse'
}

export function Logo({ size = 'md', tone = 'ink' }: LogoProps) {
  const markSize = size === 'sm' ? 20 : size === 'md' ? 24 : 32
  return (
    <span className={`${classes.logo} ${classes[size]} ${classes[tone]}`}>
      <LogoMark size={markSize} tone={tone} />
      <span className={classes.word}>{brand.name}</span>
    </span>
  )
}
