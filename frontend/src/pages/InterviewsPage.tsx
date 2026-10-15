import enGb from '@fullcalendar/core/locales/en-gb'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import { Alert, Anchor, Badge, Button, Group, Modal, MultiSelect, Paper, SegmentedControl, Select, Stack, Text, TextInput } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../api'
import { isAdmin, useMe } from '../auth'
import { ConfirmModal } from '../components/ConfirmModal'
import { interviewsKey, statusColor, useInterviews, type Interview, type InterviewStatus } from '../interviews'
import { useSections } from '../sections'
import { EmptyState, PageHeader, TableSkeleton } from '../components/Page'
import { IconCalendarEvent } from '@tabler/icons-react'
import '../calendar.css'

const day = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })
const time = (iso: string) => new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
// <input type="datetime-local"> works in local time without zone: "YYYY-MM-DDTHH:mm"
const toLocalInput = (iso: string) => {
  const d = new Date(iso)
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

export function InterviewsPage() {
  const me = useMe()
  const admin = isAdmin(me.data)
  const isPhone = useMediaQuery('(max-width: 48em)')
  const sections = useSections()
  const [view, setView] = useState<'upcoming' | 'all' | 'calendar'>('upcoming')
  const [sectionId, setSectionId] = useState<string | null>(null)
  const [now] = useState(() => new Date().toISOString())
  const interviews = useInterviews({
    sectionId: sectionId ?? undefined,
    ...(view === 'upcoming' ? { from: now, status: 'Scheduled' } : {}),
  })
  const [selected, setSelected] = useState<Interview | null>(null)

  const byDay = new Map<string, Interview[]>()
  for (const i of interviews.data ?? []) byDay.set(day(i.start), [...(byDay.get(day(i.start)) ?? []), i])

  return (
    <Stack>
      <PageHeader title={admin ? 'Interviews' : 'My interviews'}>
        {admin && (
          <Select placeholder="All sections" clearable value={sectionId} onChange={setSectionId} data={(sections.data ?? []).map((s) => ({ value: String(s.id), label: s.name }))} />
        )}
        <SegmentedControl
          value={view}
          onChange={(v) => setView(v as typeof view)}
          data={[
            { value: 'upcoming', label: 'Upcoming' },
            { value: 'all', label: 'All' },
            { value: 'calendar', label: 'Calendar' },
          ]}
        />
      </PageHeader>

      {interviews.isPending && <TableSkeleton />}
      {interviews.isError && <Alert color="red">{errorMessage(interviews.error)}</Alert>}
      {interviews.data?.length === 0 && view !== 'calendar' && (
        <EmptyState icon={<IconCalendarEvent size={24} />} title={view === 'upcoming' ? 'No upcoming interviews' : 'No interviews yet'}>
          {admin
            ? 'Interviews appear here as soon as recruits book a time through their links.'
            : "When a recruit books a time you're on the team for, it shows up here. Keep your availability up to date."}
        </EmptyState>
      )}

      {view === 'calendar' ? (
        <FullCalendar
          plugins={[timeGridPlugin]}
          locale={enGb}
          initialView={isPhone ? 'timeGrid3Day' : 'timeGridWeek'}
          key={isPhone ? 'phone' : 'desktop'}
          views={{ timeGrid3Day: { type: 'timeGrid', duration: { days: 3 } } }}
          headerToolbar={{ left: 'prev,next today', center: 'title', right: '' }}
          initialDate={interviews.data?.find((i) => i.start >= now)?.start}
          firstDay={1}
          allDaySlot={false}
          nowIndicator
          height="auto"
          slotMinTime="07:00:00"
          slotMaxTime="23:00:00"
          slotLabelFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
          eventTimeFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
          events={(interviews.data ?? []).map((i) => ({
            id: String(i.id),
            start: i.start,
            end: i.end,
            title: `${i.recruit.fullName} · ${i.sections.map((s) => s.name).join(' + ')}`,
            color: `var(--mantine-color-${statusColor[i.status]}-6)`,
          }))}
          eventClick={(e) => setSelected(interviews.data!.find((i) => String(i.id) === e.event.id)!)}
        />
      ) : (
        [...byDay].map(([d, list]) => (
          <Stack key={d} gap="xs">
            <Text fw={600}>{d}</Text>
            {list.map((i) => (
              <InterviewCard key={i.id} interview={i} admin={admin} onEdit={() => setSelected(i)} />
            ))}
          </Stack>
        ))
      )}

      <Modal opened={selected !== null} onClose={() => setSelected(null)} title="Interview" size="lg" centered>
        {selected && (admin ? <EditInterview key={selected.id} interview={selected} onDone={() => setSelected(null)} /> : <InterviewCard interview={selected} admin={false} />)}
      </Modal>
    </Stack>
  )
}

function InterviewCard({ interview: i, admin, onEdit }: { interview: Interview; admin: boolean; onEdit?: () => void }) {
  return (
    <Paper withBorder p="sm" opacity={i.status === 'Cancelled' ? 0.6 : 1}>
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={4}>
          <Group gap="xs">
            <Text fw={600}>
              {time(i.start)}–{time(i.end)}
            </Text>
            <Text>{i.recruit.fullName}</Text>
            {i.status !== 'Scheduled' && (
              <Badge color={statusColor[i.status]} variant="light">
                {i.status}
              </Badge>
            )}
          </Group>
          <Group gap={4}>
            {i.sections.map((s) => (
              <Badge key={s.id} variant="outline" size="sm">
                {s.name}
              </Badge>
            ))}
            <Text size="sm" c="dimmed">
              with {i.interviewers.map((p) => p.name).join(' + ')}
              {i.interviewers.length === 1 && ' (one interviewer)'}
            </Text>
          </Group>
          <Text size="sm">
            <Anchor href={`mailto:${i.recruit.email}`}>{i.recruit.email}</Anchor>
            {i.recruit.phone && ` · ${i.recruit.phone}`}
            {i.recruit.discordNickname && ` · Discord: ${i.recruit.discordNickname}`}
          </Text>
        </Stack>
        {admin && onEdit && (
          <Button size="xs" variant="subtle" onClick={onEdit}>
            Edit
          </Button>
        )}
      </Group>
    </Paper>
  )
}

function EditInterview({ interview: i, onDone }: { interview: Interview; onDone: () => void }) {
  const queryClient = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: () => api<{ id: string; displayName: string }[]>('/api/users') })
  const [start, setStart] = useState(toLocalInput(i.start))
  const [interviewerIds, setInterviewerIds] = useState(i.interviewers.map((p) => p.id))
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const deleteRecruit = useMutation({
    mutationFn: () => api(`/api/recruits/${i.recruit.id}`, { method: 'DELETE' }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: interviewsKey })
      queryClient.invalidateQueries({ queryKey: ['booking-links'] })
      onDone()
    },
  })

  const update = useMutation({
    mutationFn: (body: { start?: string; interviewerIds?: string[]; status?: InterviewStatus }) =>
      api(`/api/interviews/${i.id}`, { method: 'PATCH', json: body }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: interviewsKey })
      queryClient.invalidateQueries({ queryKey: ['booking-links'] }) // link status changes on cancel
      onDone()
    },
  })
  const moved = start !== toLocalInput(i.start)

  return (
    <Stack>
      <InterviewCard interview={i} admin={false} />
      {update.isError && !confirmCancel && <Alert color="red">{errorMessage(update.error)}</Alert>}
      <form
        onSubmit={(e) => {
          e.preventDefault()
          update.mutate({ interviewerIds, ...(moved ? { start: new Date(start).toISOString() } : {}) })
        }}
      >
        <Stack>
          <TextInput
            type="datetime-local"
            label="Start"
            description="Moving keeps the length and buffer. Interviewers' availability isn't checked – only that the room is free."
            value={start}
            onChange={(e) => setStart(e.currentTarget.value)}
            required
          />
          <MultiSelect
            label="Interviewers"
            data={(users.data ?? []).map((u) => ({ value: u.id, label: u.displayName }))}
            value={interviewerIds}
            onChange={setInterviewerIds}
            error={fieldError(update.error, 'InterviewerIds')}
            searchable
            required
          />
          <Group justify="space-between">
            <Group gap="xs">
              {i.status === 'Scheduled' && (
                <>
                  <Button variant="subtle" color="red" onClick={() => (update.reset(), setConfirmCancel(true))}>
                    Cancel interview
                  </Button>
                  <Button variant="subtle" color="gray" loading={update.isPending && update.variables?.status === 'Done'} onClick={() => update.mutate({ status: 'Done' })}>
                    Mark done
                  </Button>
                </>
              )}
              {i.status !== 'Scheduled' && (
                <Button variant="subtle" loading={update.isPending && update.variables?.status === 'Scheduled'} onClick={() => update.mutate({ status: 'Scheduled' })}>
                  Restore
                </Button>
              )}
            </Group>
            <Button type="submit" loading={update.isPending && !update.variables?.status} disabled={!interviewerIds.length}>
              Save
            </Button>
          </Group>
        </Stack>
      </form>
      <Group justify="flex-end">
        <Button variant="subtle" color="red" size="xs" onClick={() => (deleteRecruit.reset(), setConfirmDelete(true))}>
          Delete recruit's data
        </Button>
      </Group>
      <ConfirmModal
        opened={confirmDelete}
        title="Delete recruit's data"
        message={
          <>
            Permanently delete <b>{i.recruit.fullName}</b> ({i.recruit.email}) with all their interviews, applications and booking links? Use this when they
            ask to be removed (GDPR). This can't be undone.
          </>
        }
        confirmLabel="Delete permanently"
        loading={deleteRecruit.isPending}
        error={deleteRecruit.error}
        onConfirm={() => deleteRecruit.mutate()}
        onClose={() => setConfirmDelete(false)}
      />
      <ConfirmModal
        opened={confirmCancel}
        title="Cancel interview"
        message={
          <>
            Cancel the interview with <b>{i.recruit.fullName}</b>? The time becomes free and their link can be used to book again. Let them know yourself.
          </>
        }
        confirmLabel="Cancel interview"
        loading={update.isPending}
        error={update.error}
        onConfirm={() => update.mutate({ status: 'Cancelled' })}
        onClose={() => setConfirmCancel(false)}
      />
    </Stack>
  )
}
