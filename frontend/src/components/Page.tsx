import { Group, Paper, Skeleton, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import type { ReactNode } from 'react'

// Same header on every panel page: title, optional one-line explanation, actions on the right.
export function PageHeader({ title, description, children }: { title: string; description?: ReactNode; children?: ReactNode }) {
  return (
    <Group justify="space-between" align="flex-end" gap="sm" mb="xs">
      <Stack gap={4} maw={640}>
        <Title order={2}>{title}</Title>
        {description && (
          <Text c="dimmed" size="sm">
            {description}
          </Text>
        )}
      </Stack>
      {children && <Group gap="sm">{children}</Group>}
    </Group>
  )
}

// Nothing to show yet → say what to do next.
export function EmptyState({ icon, title, children }: { icon: ReactNode; title: string; children?: ReactNode }) {
  return (
    <Paper withBorder p="xl">
      <Stack align="center" gap="xs" ta="center">
        <ThemeIcon variant="light" size={48} radius="xl">
          {icon}
        </ThemeIcon>
        <Text fw={600}>{title}</Text>
        {children && (
          <Text size="sm" c="dimmed" maw={420}>
            {children}
          </Text>
        )}
      </Stack>
    </Paper>
  )
}

// Placeholder rows while a table loads.
export function TableSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <Stack gap="xs">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} height={36} />
      ))}
    </Stack>
  )
}
