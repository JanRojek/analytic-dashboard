import { Link } from 'react-router-dom'
import { Button } from '@mantine/core'
import { Signpost } from 'lucide-react'
import { EmptyState } from '../components/EmptyState'
import classes from './NotFoundPage.module.css'

type NotFoundPageProps = {
  title?: string
  description?: string
  backTo?: { to: string; label: string }
}

export function NotFoundPage({
  title = 'Page not found',
  description = "The page you're looking for doesn't exist or was moved.",
  backTo = { to: '/projects', label: 'Back to projects' },
}: NotFoundPageProps) {
  return (
    <div className={classes.root}>
      <EmptyState
        variant="plain"
        size="lg"
        icon={<Signpost size={22} />}
        title={title}
        description={description}
        actions={
          <Button component={Link} to={backTo.to} variant="default">
            {backTo.label}
          </Button>
        }
      />
    </div>
  )
}
