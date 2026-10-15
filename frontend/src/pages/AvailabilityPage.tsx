import enGb from '@fullcalendar/core/locales/en-gb'
import type { DateSelectArg, EventClickArg } from '@fullcalendar/core'
import interactionPlugin from '@fullcalendar/interaction'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import { Alert, Group, SegmentedControl, Stack, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../api'
import { ConfirmModal } from '../components/ConfirmModal'
import '../calendar.css'

type Block = { id: number; start: string; end: string }
type Range = { start: Date; end: Date }

const time = (d: Date) => d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })

// ponytail: shown in the browser's time zone (= club time zone for local members); a named-timezone plugin if that changes.
export function AvailabilityPage() {
  const isPhone = useMediaQuery('(max-width: 48em)')
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<'add' | 'remove'>('add')
  const [range, setRange] = useState<Range | null>(null) // visible dates
  const [removing, setRemoving] = useState<Range | null>(null)

  const key = ['availability', range?.start.toISOString(), range?.end.toISOString()]
  const blocks = useQuery({
    queryKey: key,
    queryFn: () => api<Block[]>(`/api/availability?from=${range!.start.toISOString()}&to=${range!.end.toISOString()}`),
    enabled: !!range,
    placeholderData: keepPreviousData, // no flicker when switching weeks
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['availability'] })

  const add = useMutation({
    mutationFn: (r: Range) => api('/api/availability', { method: 'POST', json: { start: r.start.toISOString(), end: r.end.toISOString() } }),
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: (r: Range) => api(`/api/availability?start=${r.start.toISOString()}&end=${r.end.toISOString()}`, { method: 'DELETE' }),
    onSuccess: () => (refresh(), setRemoving(null)),
  })

  const onSelect = (s: DateSelectArg) => {
    s.view.calendar.unselect()
    const r = { start: s.start, end: s.end }
    if (mode === 'add') return add.mutate(r)
    remove.reset()
    setRemoving(r)
  }
  const onEventClick = (e: EventClickArg) => {
    remove.reset()
    setRemoving({ start: e.event.start!, end: e.event.end! })
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>My availability</Title>
        <SegmentedControl
          value={mode}
          onChange={(v) => setMode(v as typeof mode)}
          data={[
            { value: 'add', label: 'Add time' },
            { value: 'remove', label: 'Remove time' },
          ]}
        />
      </Group>
      <Text c="dimmed" size="sm">
        {mode === 'add' ? 'Drag over the calendar to mark when you can interview.' : 'Drag over a period to remove it from your availability.'} Click a block to
        remove it. {isPhone && 'On a phone, press and hold, then drag.'}
      </Text>
      {(add.isError || blocks.isError) && <Alert color="red">{errorMessage(add.error ?? blocks.error)}</Alert>}

      <FullCalendar
        plugins={[timeGridPlugin, interactionPlugin]}
        locale={enGb}
        initialView={isPhone ? 'timeGrid3Day' : 'timeGridWeek'}
        key={isPhone ? 'phone' : 'desktop'} // re-mount when the view type changes
        views={{ timeGrid3Day: { type: 'timeGrid', duration: { days: 3 }, buttonText: '3 days' } }}
        headerToolbar={{ left: 'prev,next today', center: 'title', right: '' }}
        firstDay={1}
        allDaySlot={false}
        nowIndicator
        height="auto"
        slotMinTime="07:00:00"
        slotMaxTime="23:00:00"
        slotDuration="00:30:00"
        snapDuration="00:15:00"
        slotLabelFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
        eventTimeFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
        selectable
        selectMirror
        selectLongPressDelay={300}
        selectAllow={(s) => s.start >= new Date()}
        select={onSelect}
        datesSet={(d) => setRange({ start: d.start, end: d.end })}
        events={(blocks.data ?? []).map((b) => ({ id: String(b.id), start: b.start, end: b.end, title: 'Available', color: 'var(--mantine-color-green-6)' }))}
        eventClick={onEventClick}
      />

      <ConfirmModal
        opened={removing !== null}
        title="Remove availability"
        message={
          removing && (
            <>
              Remove {removing.start.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })}, {time(removing.start)}–{time(removing.end)}?
            </>
          )
        }
        confirmLabel="Remove"
        loading={remove.isPending}
        error={remove.error}
        onConfirm={() => removing && remove.mutate(removing)}
        onClose={() => setRemoving(null)}
      />
    </Stack>
  )
}
