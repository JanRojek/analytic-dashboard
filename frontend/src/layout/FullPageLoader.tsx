import { Loader } from '@mantine/core'
import { LogoMark } from '../brand/Logo'
import classes from './FullPageLoader.module.css'

export function FullPageLoader({ label = 'Loading' }: { label?: string }) {
  return (
    <div className={classes.root} role="status" aria-live="polite">
      <LogoMark size={28} />
      <span className={classes.label}>
        <Loader size={14} color="gray" type="dots" />
        {label}
      </span>
    </div>
  )
}
