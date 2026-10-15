import { Alert, Anchor, Badge, Button, Checkbox, Chip, Container, Group, Loader, Paper, SimpleGrid, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError, errorMessage, fieldError } from '../api'

type LinkInfo = {
  sections: string[]
  durationMinutes: number
  contactEmail: string | null
  location: string | null
  interviewsFrom: string | null
  interviewsTo: string | null
  name: string | null
  email: string | null
  booking: { start: string; end: string } | null
}
type Slot = { start: string; end: string; color: 'Green' | 'Yellow' }

const dayKey = (iso: string) => new Date(iso).toLocaleDateString('en-CA') // YYYY-MM-DD, local
const dayLabel = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
const longDay = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })
const time = (iso: string) => new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })

// Public page – recruits have no account; the token in the URL is the key.
export function BookingPage() {
  const { token } = useParams()
  const base = `/api/public/links/${token}`
  const info = useQuery({ queryKey: ['public-link', token], queryFn: () => api<LinkInfo>(base), retry: false })

  return (
    <Container size="sm" py="xl" px="md">
      <Stack>
        <Title order={2}>Book your interview</Title>
        {info.isPending && <Loader />}
        {info.isError && (
          <Alert color="red" title={info.error instanceof ApiError && info.error.status === 410 ? 'This link has expired' : 'This link is not valid'}>
            Please contact the person who sent it to you.
          </Alert>
        )}
        {info.data && (info.data.booking ? <Booked info={info.data} /> : <Booking info={info.data} base={base} />)}
      </Stack>
    </Container>
  )
}

function Contact({ info, children }: { info: LinkInfo; children: React.ReactNode }) {
  if (!info.contactEmail) return null
  return (
    <Text size="sm">
      {children} <Anchor href={`mailto:${info.contactEmail}`}>{info.contactEmail}</Anchor>.
    </Text>
  )
}

function Header({ info }: { info: LinkInfo }) {
  return (
    <Group gap="xs">
      {info.sections.map((s) => (
        <Badge key={s} size="lg" variant="light">
          {s}
        </Badge>
      ))}
      <Text size="sm" c="dimmed">
        {info.durationMinutes} min{info.location && ` · ${info.location}`}
      </Text>
    </Group>
  )
}

function Booked({ info }: { info: LinkInfo }) {
  const b = info.booking!
  return (
    <Stack>
      <Header info={info} />
      <Alert color="green" title="Your interview is booked">
        <Text fw={500}>
          {longDay(b.start)}, {time(b.start)}–{time(b.end)}
        </Text>
        {info.location && <Text>{info.location}</Text>}
      </Alert>
      <Contact info={info}>Need to change it? Write to us at</Contact>
    </Stack>
  )
}

function Booking({ info, base }: { info: LinkInfo; base: string }) {
  const queryClient = useQueryClient()
  const [showYellow, setShowYellow] = useState(false)
  const [day, setDay] = useState<string | null>(null)
  const [picked, setPicked] = useState<Slot | null>(null)

  const slots = useQuery({
    queryKey: ['public-slots', base, showYellow],
    queryFn: () => api<Slot[]>(`${base}/slots?includeYellow=${showYellow}`),
  })

  const days = [...new Set((slots.data ?? []).map((s) => dayKey(s.start)))]
  const selectedDay = day && days.includes(day) ? day : days[0]
  const daySlots = (slots.data ?? []).filter((s) => dayKey(s.start) === selectedDay)

  return (
    <Stack>
      <Header info={info} />
      <Text>Pick a time that suits you. Times are shown in your local time.</Text>

      {slots.isPending && <Loader />}
      {slots.isError && <Alert color="red">{errorMessage(slots.error)}</Alert>}
      {slots.data?.length === 0 && (
        <Alert color="gray">{showYellow ? 'There are no free times at the moment.' : 'There are no free times that fit at the moment.'}</Alert>
      )}

      {days.length > 0 && (
        <Chip.Group value={selectedDay} onChange={(v) => (setDay(v as string), setPicked(null))}>
          <Group gap="xs">
            {days.map((d) => (
              <Chip key={d} value={d}>
                {dayLabel(`${d}T12:00:00`)}
              </Chip>
            ))}
          </Group>
        </Chip.Group>
      )}
      {daySlots.length > 0 && (
        <SimpleGrid cols={{ base: 3, xs: 4, sm: 6 }} spacing="xs">
          {daySlots.map((s) => (
            <Button
              key={s.start}
              variant={picked?.start === s.start ? 'filled' : 'light'}
              color={s.color === 'Green' ? 'green' : 'yellow'}
              onClick={() => setPicked(s)}
            >
              {time(s.start)}
            </Button>
          ))}
        </SimpleGrid>
      )}

      <Switch
        label="None of these times suit me"
        checked={showYellow}
        onChange={(e) => (setShowYellow(e.currentTarget.checked), setPicked(null))}
      />
      {showYellow && (
        <Stack gap={4}>
          <Text size="sm">
            Times marked in <b>yellow</b> are also possible, but fewer of us are available then – please use them only if no green time works.
          </Text>
          <Contact info={info}>If none of these times suit you either, contact us at</Contact>
        </Stack>
      )}

      {picked && (
        <BookingForm
          key={picked.start}
          info={info}
          base={base}
          slot={picked}
          onConflict={() => {
            setPicked(null)
            queryClient.invalidateQueries({ queryKey: ['public-slots', base] })
          }}
        />
      )}
    </Stack>
  )
}

function BookingForm({ info, base, slot, onConflict }: { info: LinkInfo; base: string; slot: Slot; onConflict: () => void }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState({ fullName: info.name ?? '', email: info.email ?? '', discordNickname: '', phone: '', gdprConsent: false })
  const [conflict, setConflict] = useState<string | null>(null)

  const book = useMutation({
    mutationFn: () => api(`${base}/bookings`, { method: 'POST', json: { start: slot.start, ...form } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['public-link'] }), // page switches to "booked"
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) {
        setConflict(e.message)
        onConflict()
      }
    },
  })

  return (
    <Paper withBorder p="md">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          book.mutate()
        }}
      >
        <Stack>
          <Text fw={500}>
            {longDay(slot.start)}, {time(slot.start)}–{time(slot.end)}
          </Text>
          {conflict && <Alert color="orange">{conflict}</Alert>}
          {book.isError && !conflict && !fieldError(book.error, 'FullName') && !fieldError(book.error, 'Email') && (
            <Alert color="red">{errorMessage(book.error)}</Alert>
          )}
          <TextInput label="Full name" required autoComplete="name" value={form.fullName} error={fieldError(book.error, 'FullName')} onChange={(e) => setForm({ ...form, fullName: e.currentTarget.value })} />
          <TextInput label="E-mail" type="email" required autoComplete="email" value={form.email} error={fieldError(book.error, 'Email')} onChange={(e) => setForm({ ...form, email: e.currentTarget.value })} />
          <TextInput
            label="Discord nickname"
            description="Optional, but recommended – we talk to members on Discord."
            placeholder="e.g. jan_kowalski"
            autoComplete="off"
            value={form.discordNickname}
            error={fieldError(book.error, 'DiscordNickname')}
            onChange={(e) => setForm({ ...form, discordNickname: e.currentTarget.value })}
          />
          <TextInput label="Phone (optional)" type="tel" autoComplete="tel" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.currentTarget.value })} />
          <Checkbox
            required
            checked={form.gdprConsent}
            onChange={(e) => setForm({ ...form, gdprConsent: e.currentTarget.checked })}
            label="I agree to the processing of my personal data for the purpose of this recruitment."
          />
          <Button type="submit" loading={book.isPending}>
            Book this time
          </Button>
        </Stack>
      </form>
    </Paper>
  )
}
