import { Outlet } from 'react-router-dom'
import { TopBar } from './TopBar'
import classes from './AppFrame.module.css'

/** Global chrome: a stable top bar; everything else is route-specific. */
export function AppFrame() {
  return (
    <div className={classes.frame}>
      <TopBar />
      <div className={classes.body}>
        <Outlet />
      </div>
    </div>
  )
}
