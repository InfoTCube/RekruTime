import { Box, Container, Group, SegmentedControl, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'
import { dictionaries, type Dictionary } from '../locales'
import { LogoMark } from './Logo'
import { SignalTrace } from './SignalTrace'

type HeaderProps = { club: string | null | undefined; title: string; t: Dictionary; language: string; onLanguage: (l: string) => void; children?: ReactNode }

// Hero band of the recruit-facing pages: who's inviting, what for – and the one animated moment, the signal trace.
export function PublicHeader({ club, title, t, language, onLanguage, children }: HeaderProps) {
  return (
    <Box bg="cortex.9" c="white">
      <Container size="sm" px="md" pt="lg">
        <Group justify="space-between" wrap="nowrap" mb={40}>
          <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
            <LogoMark size={26} />
            <Text fw={600} truncate c="cortex.0">
              {club ? `${t.recruitment} – ${club}` : t.recruitment}
            </Text>
          </Group>
          <SegmentedControl
            size="xs"
            value={language}
            onChange={onLanguage}
            color="signal.8"
            styles={{ root: { background: 'rgba(255,255,255,0.1)' }, label: { color: 'white' } }}
            data={Object.entries(dictionaries).map(([value, d]) => ({ value, label: value.toUpperCase(), title: d.name }))}
          />
        </Group>
        <Title order={1} c="white" fz={{ base: 30, sm: 40 }} lh={1.1} maw={520}>
          {title}
        </Title>
        {children}
      </Container>
      <Box pt="md">
        <SignalTrace height={44} />
      </Box>
    </Box>
  )
}
