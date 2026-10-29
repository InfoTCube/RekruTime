import { List, Paper, Stack, Text, ThemeIcon, Title } from '@mantine/core'
import { IconMessageQuestion } from '@tabler/icons-react'
import type { Prep } from '../interviews'

const day = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

// Interview prep: suggested questions of the sections, then what the recruit answered in our forms (follow-ups under the answers).
export function PrepPanel({ prep }: { prep: Prep }) {
  const questions = prep.sections.filter((s) => s.questions.length)
  return (
    <Stack>
      <Title order={3}>Suggested questions</Title>
      {questions.length === 0 && (
        <Text c="dimmed" size="sm">
          None yet – section leaders add them on the Preselection page.
        </Text>
      )}
      {questions.map((s) => (
        <Paper key={s.id} withBorder p="sm">
          <Text size="sm" fw={600} mb={4}>
            {s.name}
          </Text>
          <List size="sm" spacing={4}>
            {s.questions.map((q, i) => (
              <List.Item key={i}>{q}</List.Item>
            ))}
          </List>
        </Paper>
      ))}

      <Title order={3} mt="sm">
        Form answers
      </Title>
      {prep.forms.length === 0 && (
        <Text c="dimmed" size="sm">
          No form answers – this recruit didn't apply through a RekruTime form.
        </Text>
      )}
      {prep.forms.map((f) => (
        <Paper key={`${f.formId}-${f.submittedAt}`} withBorder p="sm">
          <Text size="xs" c="dimmed" mb="xs">
            {f.title} · {day(f.submittedAt)}
          </Text>
          <Stack gap="sm">
            {f.answers.map((a, i) => (
              <div key={i}>
                <Text size="xs" c="dimmed" fw={600}>
                  {a.question}
                </Text>
                <Text size="sm" style={{ whiteSpace: 'pre-line' }}>
                  {a.values.join(', ')}
                </Text>
                {a.followUp && (
                  <Text size="sm" c="cortex" mt={2} style={{ display: 'flex', gap: 6, alignItems: 'flex-start' }}>
                    <ThemeIcon size={18} variant="light" radius="xl" style={{ flexShrink: 0 }}>
                      <IconMessageQuestion size={12} />
                    </ThemeIcon>
                    {a.followUp}
                  </Text>
                )}
              </div>
            ))}
          </Stack>
        </Paper>
      ))}
    </Stack>
  )
}
