import { notifications } from '@mantine/notifications'
import { IconCheck } from '@tabler/icons-react'
import type { ReactNode } from 'react'

// Success feedback as a toast instead of inline green alerts.
export const notifySuccess = (message: ReactNode, title?: string) =>
  notifications.show({ color: 'signal', icon: <IconCheck size={18} />, title, message })
