import { Alert, Button, Group, Modal, Stack, Text } from '@mantine/core'
import { errorMessage } from '../api'

type Props = {
  opened: boolean
  title: string
  message: React.ReactNode
  confirmLabel: string
  color?: string
  loading: boolean
  error: unknown
  onConfirm: () => void
  onClose: () => void
}

// In-page confirmation (no window.confirm), shows the API error if the action fails.
export function ConfirmModal({ opened, title, message, confirmLabel, color = 'red', loading, error, onConfirm, onClose }: Props) {
  return (
    <Modal opened={opened} onClose={onClose} title={title} centered>
      <Stack>
        <Text>{message}</Text>
        {!!error && <Alert color="red">{errorMessage(error)}</Alert>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button color={color} loading={loading} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
