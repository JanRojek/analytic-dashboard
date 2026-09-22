import { useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { Menu } from '@mantine/core'
import { Check, ChevronDown, ChevronRight, FolderKanban, LogOut, Plus } from 'lucide-react'
import { Logo } from '../brand/Logo'
import { useAuth } from '../features/auth/AuthProvider'
import { useProject, useProjectsPage } from '../features/projects/hooks'
import { useDataset } from '../features/data/hooks'
import { useDashboard } from '../features/dashboards/hooks'
import { initials } from '../lib/format'
import { cx } from '../lib/cx'
import classes from './TopBar.module.css'

export function TopBar() {
  return (
    <header className={classes.root}>
      <div className={classes.left}>
        <Link to="/projects" className={classes.logo} aria-label="Cairn — all projects">
          <Logo size="sm" />
        </Link>
        <Breadcrumbs />
      </div>
      <div className={classes.right}>
        <UserMenu />
      </div>
    </header>
  )
}

type Crumb = { label: string; to?: string; switcher?: boolean; pending?: boolean }

function Breadcrumbs() {
  const params = useParams<{ projectId?: string; datasetId?: string; dashboardId?: string }>()
  const location = useLocation()
  const project = useProject(params.projectId)
  const dataset = useDataset(params.projectId, params.datasetId)
  const dashboard = useDashboard(params.projectId, params.dashboardId)

  const crumbs: Crumb[] = [{ label: 'Projects', to: '/projects' }]

  if (params.projectId) {
    const base = `/projects/${params.projectId}`
    crumbs.push({
      label: project.data?.name ?? 'Project',
      to: base,
      switcher: true,
      pending: project.isPending,
    })
    const section = location.pathname.split('/')[3]
    if (section === 'data') crumbs.push({ label: 'Data', to: `${base}/data` })
    if (section === 'explore') crumbs.push({ label: 'Explore', to: `${base}/explore` })
    if (section === 'dashboards') crumbs.push({ label: 'Dashboards', to: `${base}/dashboards` })
    if (params.datasetId) {
      crumbs.push({ label: dataset.data?.name ?? 'Dataset', pending: dataset.isPending })
    }
    if (params.dashboardId) {
      crumbs.push({ label: dashboard.data?.name ?? 'Dashboard', pending: dashboard.isPending })
    }
    if (location.pathname.endsWith('/data/import')) crumbs.push({ label: 'Import data' })
  }

  return (
    <nav aria-label="Breadcrumb" className={classes.breadcrumbs}>
      <ol className={classes.crumbList}>
        {crumbs.map((crumb, index) => {
          const isLast = index === crumbs.length - 1
          return (
            <li
              key={`${crumb.label}-${index}`}
              className={cx(classes.crumb, isLast && classes.crumbCurrent, index === 0 && classes.crumbRoot)}
            >
              {index > 0 && <ChevronRight size={14} className={classes.separator} aria-hidden="true" />}
              {crumb.switcher && params.projectId ? (
                <ProjectSwitcher currentId={params.projectId} label={crumb.label} pending={crumb.pending} isCurrent={isLast} />
              ) : crumb.to && !isLast ? (
                <Link to={crumb.to} className={classes.crumbLink}>
                  {crumb.label}
                </Link>
              ) : (
                <span
                  className={cx(classes.crumbText, crumb.pending && classes.pending)}
                  aria-current={isLast ? 'page' : undefined}
                >
                  {crumb.label}
                </span>
              )}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function ProjectSwitcher({
  currentId,
  label,
  pending,
  isCurrent,
}: {
  currentId: string
  label: string
  pending?: boolean
  isCurrent: boolean
}) {
  const [opened, setOpened] = useState(false)
  const navigate = useNavigate()
  const projects = useProjectsPage(1, 24, opened)

  return (
    <Menu opened={opened} onChange={setOpened} position="bottom-start" width={260}>
      <Menu.Target>
        <button
          type="button"
          className={cx(classes.switcher, pending && classes.pending)}
          aria-current={isCurrent ? 'page' : undefined}
          aria-label={`Current project: ${label}. Switch project`}
        >
          <span className={classes.switcherLabel}>{label}</span>
          <ChevronDown size={14} className={classes.switcherChevron} aria-hidden="true" />
        </button>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Switch project</Menu.Label>
        {projects.isPending && <Menu.Item disabled>Loading…</Menu.Item>}
        {projects.data?.items.map((item) => (
          <Menu.Item
            key={item.id}
            onClick={() => navigate(`/projects/${item.id}`)}
            rightSection={item.id === currentId ? <Check size={14} /> : undefined}
          >
            <span className="truncate" style={{ display: 'block', maxWidth: 190 }}>
              {item.name}
            </span>
          </Menu.Item>
        ))}
        <Menu.Divider />
        <Menu.Item component={Link} to="/projects" leftSection={<FolderKanban size={14} />}>
          All projects
        </Menu.Item>
        <Menu.Item component={Link} to="/projects?new=1" leftSection={<Plus size={14} />}>
          New project
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}

function UserMenu() {
  const { user, signOut } = useAuth()
  const navigate = useNavigate()
  if (!user) return null

  return (
    <Menu position="bottom-end" width={240}>
      <Menu.Target>
        <button type="button" className={classes.userButton} aria-label={`Account: ${user.displayName}`}>
          <span className={classes.avatar} aria-hidden="true">
            {initials(user.displayName)}
          </span>
          <span className={classes.userName}>{user.displayName}</span>
          <ChevronDown size={14} className={classes.switcherChevron} aria-hidden="true" />
        </button>
      </Menu.Target>
      <Menu.Dropdown>
        <div className={classes.userHeader}>
          <div className={classes.userHeaderName}>{user.displayName}</div>
          <div className={classes.userHeaderEmail}>{user.email}</div>
        </div>
        <Menu.Divider />
        <Menu.Item component={Link} to="/projects" leftSection={<FolderKanban size={14} />}>
          All projects
        </Menu.Item>
        <Menu.Divider />
        <Menu.Item
          leftSection={<LogOut size={14} />}
          onClick={() => {
            void signOut().then(() => navigate('/sign-in', { replace: true }))
          }}
        >
          Sign out
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
