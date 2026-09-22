import { useEffect, useState, type ReactNode } from 'react'
import { Button, Group, Modal, Stack, Text, TextInput } from '@mantine/core'

type ConfirmDialogProps = {
  opened: boolean
  onClose: () => void
  onConfirm: () => void | Promise<void>
  title: string
  message: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
  loading?: boolean
  /** When set, the user must type this text to enable the destructive action. */
  confirmText?: string
}

export function ConfirmDialog({
  opened,
  onClose,
  onConfirm,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  danger = false,
  loading = false,
  confirmText,
}: ConfirmDialogProps) {
  const [typed, setTyped] = useState('')

  useEffect(() => {
    if (!opened) setTyped('')
  }, [opened])

  const blocked = confirmText !== undefined && typed.trim() !== confirmText

  return (
    <Modal opened={opened} onClose={onClose} title={title} size={440} closeOnClickOutside={!loading}>
      <Stack gap="md">
        <Text size="sm" c="var(--app-text-2)" component="div">
          {message}
        </Text>
        {confirmText !== undefined && (
          <TextInput
            label={
              <>
                Type <strong>{confirmText}</strong> to confirm
              </>
            }
            value={typed}
            onChange={(event) => setTyped(event.currentTarget.value)}
            autoFocus
            data-autofocus
          />
        )}
        <Group justify="flex-end" gap="sm" mt="xs">
          <Button variant="default" onClick={onClose} disabled={loading}>
            {cancelLabel}
          </Button>
          <Button
            color={danger ? 'red' : 'ink'}
            onClick={() => void onConfirm()}
            loading={loading}
            disabled={blocked}
            data-autofocus={confirmText === undefined ? true : undefined}
          >
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
