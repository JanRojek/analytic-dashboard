import { Tooltip } from '@mantine/core'
import classes from './MockBadge.module.css'

const DEFAULT_NOTE =
  'Prototype: this capability is not backed by the API yet. Changes stay in this browser.'

/** Marks UI that is intentionally mocked so nobody mistakes it for a working integration. */
export function MockBadge({ note = DEFAULT_NOTE }: { note?: string }) {
  return (
    <Tooltip label={note} multiline w={260}>
      <span className={classes.root} tabIndex={0}>
        Prototype
      </span>
    </Tooltip>
  )
}
