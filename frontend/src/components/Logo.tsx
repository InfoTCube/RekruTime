import { Group, Text } from '@mantine/core'

// The mark: a signal trace whose spike is a check mark – a pulse, and a booked slot.
export function LogoMark({ size = 28 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
      <rect width="32" height="32" rx="8" fill="#26235c" />
      <path d="M4.5 17.5h5l3 4 6.5-12 3 7.5h5.5" fill="none" stroke="#26c6b8" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

export function Logo({ size = 28 }: { size?: number }) {
  return (
    <Group gap={8} wrap="nowrap">
      <LogoMark size={size} />
      <Text ff="heading" fw={700} fz={size * 0.72} lh={1} style={{ letterSpacing: '-0.02em' }}>
        RekruTime
      </Text>
    </Group>
  )
}
