import { Alert, Badge, Group, Skeleton, Stack, Text, Tooltip } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../../api'
import { periodRange, useRecruitmentPeriod } from '../../recruitment'
import { slotColor } from '../../theme'

type PreviewSlot = { start: string; end: string; color: 'Green' | 'Yellow'; team: string[]; suggested: boolean }

const dayLabel = (d: Date) => d.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
const timeLabel = (d: Date) => d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })

// Every possible interview start for a link over the whole recruitment period, grouped by day.
export function LinkCoverage({ linkId }: { linkId: number }) {
  const period = useRecruitmentPeriod()
  const range = period.data && periodRange(period.data)
  const slots = useQuery({
    queryKey: ['link-coverage', linkId, range?.start.toISOString()],
    queryFn: () => api<PreviewSlot[]>(`/api/booking-links/${linkId}/slots?from=${range!.start.toISOString()}&to=${range!.end.toISOString()}`),
    enabled: !!range,
  })

  if (period.data && !range) return <Alert color="cortex">No recruitment period is set, so there are no slots.</Alert>
  if (slots.isError) return <Alert color="red">{errorMessage(slots.error)}</Alert>
  if (!slots.data) return <Skeleton height={200} />
  if (slots.data.length === 0)
    return <Alert color="orange">No free times. Ask recruiters from these sections to add availability.</Alert>

  const days = new Map<string, PreviewSlot[]>()
  for (const s of slots.data) {
    const day = dayLabel(new Date(s.start))
    days.set(day, [...(days.get(day) ?? []), s])
  }
  const green = slots.data.filter((s) => s.color === 'Green').length

  return (
    <Stack gap="md">
      <Text size="sm" c="dimmed">
        {green} green and {slots.data.length - green} yellow start times (from {period.data?.minNoticeHours ?? 12} h ahead). Hover a time to see who would interview; ★ = next to a planned interview of that team, suggested to recruits first.
      </Text>
      {[...days].map(([day, daySlots]) => (
        <Stack key={day} gap={4}>
          <Text fw={500}>{day}</Text>
          <Group gap={6}>
            {daySlots.map((s) => (
              <Tooltip key={s.start} label={s.team.join(' + ') + (s.suggested ? ' · suggested' : '')}>
                <Badge variant="light" color={slotColor[s.color]} radius="sm" leftSection={s.suggested ? '★' : undefined}>
                  {timeLabel(new Date(s.start))}
                </Badge>
              </Tooltip>
            ))}
          </Group>
        </Stack>
      ))}
    </Stack>
  )
}
