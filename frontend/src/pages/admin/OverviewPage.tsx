import { Alert, Anchor, Badge, Button, Group, Paper, Progress, SimpleGrid, Skeleton, Stack, Text, Title } from '@mantine/core'
import { IconCalendarOff, IconCalendarTime } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../api'
import { EmptyState, PageHeader } from '../../components/Page'
import { useInterviews } from '../../interviews'
import { formatDay, useRecruitmentPeriod } from '../../recruitment'

type LinkRow = { status: 'Unused' | 'Booked' | 'Expired' | 'Inactive'; invitation: { state: 'NotSent' | 'Queued' | 'Sent' | 'Failed' } }
type Capacity = { sectionId: number; name: string; green: number; withYellow: number }

const when = (iso: string) => new Date(iso).toLocaleString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export function OverviewPage() {
  const period = useRecruitmentPeriod()
  const links = useQuery({ queryKey: ['booking-links'], queryFn: () => api<LinkRow[]>('/api/booking-links') })
  const interviews = useInterviews({})
  const capacity = useQuery({ queryKey: ['sections', 'capacity'], queryFn: () => api<Capacity[]>('/api/sections/capacity') })
  const [now] = useState(() => Date.now())

  const error = links.error ?? interviews.error ?? capacity.error
  const open = period.data?.interviewsFrom && period.data.interviewsTo

  return (
    <Stack gap="lg">
      <PageHeader
        title="Overview"
        description={open ? `Interviews run ${formatDay(period.data!.interviewsFrom!)} – ${formatDay(period.data!.interviewsTo!)}.` : undefined}
      />
      {error && <Alert color="red">{errorMessage(error)}</Alert>}
      {period.data && !open && (
        <EmptyState icon={<IconCalendarOff size={24} />} title="No recruitment period yet">
          Set the interview days first – recruiters then add their availability and you can invite recruits.{' '}
          <Anchor component={Link} to="/admin/recruitment">
            Set the period
          </Anchor>
        </EmptyState>
      )}

      <Funnel links={links.data} interviews={interviews.data} />

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
        <Paper withBorder p="lg">
          <Group justify="space-between" mb="md">
            <Title order={4}>Next interviews</Title>
            <Anchor component={Link} to="/interviews" size="sm">
              All interviews
            </Anchor>
          </Group>
          {!interviews.data ? (
            <Skeleton height={120} />
          ) : (
            (() => {
              const next = interviews.data.filter((i) => i.status === 'Scheduled' && new Date(i.end).getTime() > now).slice(0, 6)
              if (next.length === 0) return <Text size="sm" c="dimmed">Nothing booked yet. Booked interviews show up here.</Text>
              return (
                <Stack gap="sm">
                  {next.map((i) => (
                    <Group key={i.id} justify="space-between" wrap="nowrap" gap="sm">
                      <Stack gap={0} style={{ minWidth: 0 }}>
                        <Text fw={500} truncate>
                          {i.recruit.fullName}
                        </Text>
                        <Text size="xs" c="dimmed" truncate>
                          {i.interviewers.map((p) => p.name).join(', ') || 'No interviewers'}
                        </Text>
                      </Stack>
                      <Stack gap={2} align="flex-end">
                        <Text size="sm" style={{ whiteSpace: 'nowrap' }}>
                          {when(i.start)}
                        </Text>
                        <Group gap={4}>
                          {i.sections.map((s) => (
                            <Badge key={s.id} size="xs" variant="light">
                              {s.name}
                            </Badge>
                          ))}
                        </Group>
                      </Stack>
                    </Group>
                  ))}
                </Stack>
              )
            })()
          )}
        </Paper>

        <Paper withBorder p="lg">
          <Title order={4}>Room left</Title>
          <Text size="sm" c="dimmed" mb="md">
            How many more interviews fit per section in the rest of the period – teal with the full team, amber counting yellow times too.
          </Text>
          {!capacity.data ? (
            <Skeleton height={120} />
          ) : capacity.data.length === 0 ? (
            <Text size="sm" c="dimmed">
              No sections yet. <Anchor component={Link} to="/admin/sections">Add a section</Anchor>
            </Text>
          ) : (
            <Stack gap="md">
              {capacity.data.map((c) => {
                const max = Math.max(...capacity.data.map((x) => x.withYellow), 1)
                return (
                  <Stack key={c.sectionId} gap={4}>
                    <Group justify="space-between">
                      <Text fw={500}>{c.name}</Text>
                      <Text size="sm" c={c.withYellow === 0 ? 'red' : undefined}>
                        {c.green} <Text span c="dimmed" size="sm">({c.withYellow} with yellow)</Text>
                      </Text>
                    </Group>
                    <Progress.Root size="md" aria-label={`${c.name}: ${c.green} interviews with the full team, ${c.withYellow} with yellow times`}>
                      <Progress.Section value={(c.green / max) * 100} color="signal" />
                      <Progress.Section value={((c.withYellow - c.green) / max) * 100} color="spark" />
                    </Progress.Root>
                  </Stack>
                )
              })}
              <Button variant="light" component={Link} to="/availability" leftSection={<IconCalendarTime size={16} />} w="fit-content">
                Add my availability
              </Button>
            </Stack>
          )}
        </Paper>
      </SimpleGrid>
    </Stack>
  )
}

// The recruitment is a sequence, so it's shown as one: each stage as a share of the links created.
function Funnel({ links, interviews }: { links?: LinkRow[]; interviews?: { status: string }[] }) {
  if (!links || !interviews) return <Skeleton height={96} />
  const stages = [
    { label: 'Links created', value: links.length, hint: 'personal booking links' },
    { label: 'Invited', value: links.filter((l) => l.invitation.state === 'Sent').length, hint: 'invitation e-mail delivered' },
    { label: 'Booked', value: links.filter((l) => l.status === 'Booked').length, hint: 'chose a time' },
    { label: 'Interviewed', value: interviews.filter((i) => i.status === 'Done').length, hint: 'marked as done' },
  ]
  const base = Math.max(stages[0].value, 1)
  return (
    <Paper withBorder p="lg">
      <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="lg">
        {stages.map((s, i) => (
          <Stack key={s.label} gap={6}>
            <Text size="sm" c="dimmed">
              {i + 1}. {s.label}
            </Text>
            <Text ff="heading" fz={34} fw={700} lh={1}>
              {s.value}
            </Text>
            <Progress value={(s.value / base) * 100} color={i === 0 ? 'cortex' : 'signal'} size="sm" aria-label={`${s.label}: ${s.value}`} />
            <Text size="xs" c="dimmed">
              {s.hint}
            </Text>
          </Stack>
        ))}
      </SimpleGrid>
    </Paper>
  )
}
