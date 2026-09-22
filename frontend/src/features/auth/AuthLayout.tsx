import { Outlet } from 'react-router-dom'
import { Logo } from '../../brand/Logo'
import { brand } from '../../brand/brand'
import classes from './AuthLayout.module.css'

/**
 * Split layout: a calm brand panel with a static product vignette on the left,
 * the form on the right. Collapses to a single column on small screens.
 */
export function AuthLayout() {
  return (
    <div className={classes.root}>
      <aside className={classes.brand}>
        <div className={classes.brandTop}>
          <Logo size="md" />
        </div>
        <div className={classes.brandBody}>
          <h1 className={classes.tagline}>{brand.tagline}</h1>
          <p className={classes.lede}>{brand.description}</p>
          <Vignette />
        </div>
        <footer className={classes.brandFooter}>
          <span>Engineering thesis prototype</span>
          <span>Project → Data → Insight → Dashboard</span>
        </footer>
      </aside>
      <main className={classes.main}>
        <div className={classes.mobileLogo}>
          <Logo size="md" />
        </div>
        <div className={classes.card}>
          <Outlet />
        </div>
      </main>
    </div>
  )
}

/** A quiet, static miniature of the product: a bar card and a stat tile. */
function Vignette() {
  const bars = [42, 58, 51, 74, 66, 88, 80]
  const width = 320
  const height = 132
  const padX = 18
  const baseline = 108
  const slot = (width - padX * 2) / bars.length
  const barWidth = 18
  const max = Math.max(...bars)
  return (
    <div className={classes.vignette} aria-hidden="true">
      <div className={classes.vCard}>
        <div className={classes.vHeader}>
          <span className={classes.vTitle}>Revenue by month</span>
          <span className={classes.vMeta}>Sum · orders.csv</span>
        </div>
        <svg viewBox={`0 0 ${width} ${height}`} className={classes.vChart}>
          {[0.25, 0.5, 0.75, 1].map((step) => (
            <line
              key={step}
              x1={padX}
              x2={width - padX}
              y1={baseline - (baseline - 16) * step}
              y2={baseline - (baseline - 16) * step}
              stroke="#e7e5e4"
              strokeWidth={1}
            />
          ))}
          <line x1={padX} x2={width - padX} y1={baseline} y2={baseline} stroke="#d6d3d1" />
          {bars.map((value, index) => {
            const h = ((baseline - 16) * value) / max
            const x = padX + slot * index + (slot - barWidth) / 2
            const y = baseline - h
            return (
              <path
                key={index}
                d={`M${x},${baseline} v${-(h - 4)} a4,4 0 0 1 4,-4 h${barWidth - 8} a4,4 0 0 1 4,4 v${h - 4} z`}
                fill={index === bars.length - 1 ? '#2a78d6' : '#9ec5f4'}
              />
            )
          })}
          {['Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep'].map((label, index) => (
            <text
              key={label}
              x={padX + slot * index + slot / 2}
              y={baseline + 16}
              textAnchor="middle"
              fontSize={9}
              fill="#78716c"
            >
              {label}
            </text>
          ))}
        </svg>
      </div>
      <div className={classes.vTile}>
        <span className={classes.vLabel}>Completeness</span>
        <span className={classes.vValue}>98.4%</span>
        <span className={classes.vMeter}>
          <span style={{ width: '98.4%' }} />
        </span>
      </div>
    </div>
  )
}
