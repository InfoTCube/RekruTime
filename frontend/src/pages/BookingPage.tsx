import { Alert, Anchor, Badge, Button, Checkbox, Chip, Collapse, Container, Group, Loader, Paper, SegmentedControl, SimpleGrid, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError, fieldError } from '../api'
import { defaultLanguage, dictionaries, type Dictionary } from '../locales'

type LinkInfo = {
  sections: string[]
  durationMinutes: number
  contactEmail: string | null
  location: string | null
  interviewsFrom: string | null
  interviewsTo: string | null
  name: string | null
  email: string | null
  language: string
  booking: { start: string; end: string } | null
}
type Slot = { start: string; end: string; color: 'Green' | 'Yellow' }

const T = createContext<Dictionary>(dictionaries[defaultLanguage])
const dayKey = (iso: string) => new Date(iso).toLocaleDateString('en-CA') // YYYY-MM-DD, local – a key, not shown

function useFormat() {
  const { locale } = useContext(T)
  return {
    dayLabel: (iso: string) => new Date(iso).toLocaleDateString(locale, { weekday: 'short', day: 'numeric', month: 'short' }),
    longDay: (iso: string) => new Date(iso).toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long' }),
    time: (iso: string) => new Date(iso).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' }),
  }
}

// The visitor's own choice is remembered in this browser; storage may be unavailable (private mode) – then just don't remember.
const LANGUAGE_KEY = 'rekrutime.booking.language'
const storedLanguage = () => {
  try {
    return localStorage.getItem(LANGUAGE_KEY)
  } catch {
    return null
  }
}

// Public page – recruits have no account; the token in the URL is the key.
export function BookingPage() {
  const { token } = useParams()
  const base = `/api/public/links/${token}`
  const info = useQuery({ queryKey: ['public-link', token], queryFn: () => api<LinkInfo>(base), retry: false })
  const [chosen, setChosen] = useState(storedLanguage)
  const language = [chosen, info.data?.language, defaultLanguage].find((l) => l && dictionaries[l])!
  const t = dictionaries[language]

  useEffect(() => {
    document.documentElement.lang = language
    document.title = t.title
  }, [language, t])

  const choose = (l: string) => {
    setChosen(l)
    try {
      localStorage.setItem(LANGUAGE_KEY, l)
    } catch {
      /* not remembered – fine */
    }
  }

  return (
    <T.Provider value={t}>
      <Container size="sm" py="xl" px="md">
        <Stack>
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Title order={2}>{t.title}</Title>
            <SegmentedControl
              size="xs"
              value={language}
              onChange={choose}
              data={Object.entries(dictionaries).map(([value, d]) => ({ value, label: value.toUpperCase(), title: d.name }))}
            />
          </Group>
          {info.isPending && <Loader />}
          {info.isError && (
            <Alert color="red" title={info.error instanceof ApiError && info.error.status === 410 ? t.linkExpired : t.linkInvalid}>
              {t.linkProblemHelp}
            </Alert>
          )}
          {info.data && (info.data.booking ? <Booked info={info.data} /> : <Booking info={info.data} base={base} />)}
        </Stack>
      </Container>
    </T.Provider>
  )
}

function Contact({ info, children }: { info: LinkInfo; children: React.ReactNode }) {
  if (!info.contactEmail) return null
  return (
    <Text size="sm">
      {children} <Anchor href={`mailto:${info.contactEmail}`}>{info.contactEmail}</Anchor>
    </Text>
  )
}

function Header({ info }: { info: LinkInfo }) {
  const t = useContext(T)
  return (
    <Group gap="xs">
      {info.sections.map((s) => (
        <Badge key={s} size="lg" variant="light">
          {s}
        </Badge>
      ))}
      <Text size="sm" c="dimmed">
        {t.minutes(info.durationMinutes)}
        {info.location && ` · ${info.location}`}
      </Text>
    </Group>
  )
}

function Booked({ info }: { info: LinkInfo }) {
  const t = useContext(T)
  const f = useFormat()
  const b = info.booking!
  return (
    <Stack>
      <Header info={info} />
      <Alert color="green" title={t.booked}>
        <Text fw={500}>
          {f.longDay(b.start)}, {f.time(b.start)}–{f.time(b.end)}
        </Text>
        {info.location && <Text>{info.location}</Text>}
      </Alert>
      <Contact info={info}>{t.needChange}</Contact>
    </Stack>
  )
}

function Booking({ info, base }: { info: LinkInfo; base: string }) {
  const t = useContext(T)
  const f = useFormat()
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
      <Text>{t.pickTime}</Text>

      {slots.isPending && <Loader />}
      {slots.isError && <Alert color="red">{t.somethingWrong}</Alert>}
      {slots.data?.length === 0 && <Alert color="gray">{showYellow ? t.noTimes : t.noGreenTimes}</Alert>}

      {days.length > 0 && (
        <Chip.Group
          value={selectedDay}
          onChange={(v) => {
            setDay(v as string)
            setPicked(null)
          }}
        >
          <Group gap="xs">
            {days.map((d) => (
              <Chip key={d} value={d}>
                {f.dayLabel(`${d}T12:00:00`)}
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
              {f.time(s.start)}
            </Button>
          ))}
        </SimpleGrid>
      )}

      <Switch
        label={t.noneSuit}
        checked={showYellow}
        onChange={(e) => {
          setShowYellow(e.currentTarget.checked)
          setPicked(null)
        }}
      />
      {showYellow && (
        <Stack gap={4}>
          <Text size="sm">{t.yellowExplained}</Text>
          <Contact info={info}>{t.contactIfNone}</Contact>
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
            queryClient.invalidateQueries({ queryKey: ['public-link'] }) // if the link got booked (e.g. other tab) → "booked" view
          }}
        />
      )}
    </Stack>
  )
}

function BookingForm({ info, base, slot, onConflict }: { info: LinkInfo; base: string; slot: Slot; onConflict: () => void }) {
  const t = useContext(T)
  const f = useFormat()
  const queryClient = useQueryClient()
  const [form, setForm] = useState({ fullName: info.name ?? '', email: info.email ?? '', discordNickname: '', phone: '', gdprConsent: false })
  const [conflict, setConflict] = useState(false)
  const [privacyOpen, setPrivacyOpen] = useState(false)

  const book = useMutation({
    mutationFn: () => api(`${base}/bookings`, { method: 'POST', json: { start: slot.start, ...form } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['public-link'] }), // page switches to "booked"
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) {
        setConflict(true)
        onConflict()
      }
    },
  })
  // Server messages are English – show our own translated ones instead.
  const invalid = (field: string, message: string) => (fieldError(book.error, field) ? message : undefined)
  const fieldProblem = ['FullName', 'Email', 'DiscordNickname', 'Phone'].some((k) => fieldError(book.error, k))

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
            {f.longDay(slot.start)}, {f.time(slot.start)}–{f.time(slot.end)}
          </Text>
          {conflict && <Alert color="orange">{t.taken}</Alert>}
          {book.isError && !conflict && !fieldProblem && <Alert color="red">{t.somethingWrong}</Alert>}
          <TextInput label={t.fullName} required autoComplete="name" value={form.fullName} error={invalid('FullName', t.invalidName)} onChange={(e) => setForm({ ...form, fullName: e.currentTarget.value })} />
          <TextInput label={t.email} type="email" required autoComplete="email" value={form.email} error={invalid('Email', t.invalidEmail)} onChange={(e) => setForm({ ...form, email: e.currentTarget.value })} />
          <TextInput
            label={t.discord}
            description={t.discordHelp}
            placeholder={t.discordPlaceholder}
            autoComplete="off"
            value={form.discordNickname}
            error={invalid('DiscordNickname', t.invalidDiscord)}
            onChange={(e) => setForm({ ...form, discordNickname: e.currentTarget.value })}
          />
          <TextInput label={t.phone} type="tel" autoComplete="tel" value={form.phone} error={invalid('Phone', t.invalidPhone)} onChange={(e) => setForm({ ...form, phone: e.currentTarget.value })} />
          <Checkbox required checked={form.gdprConsent} onChange={(e) => setForm({ ...form, gdprConsent: e.currentTarget.checked })} label={t.consent} />
          <Anchor component="button" type="button" size="sm" onClick={() => setPrivacyOpen((o) => !o)}>
            {t.privacyLink}
          </Anchor>
          <Collapse expanded={privacyOpen}>
            <Text size="sm" c="dimmed">
              {t.privacy(info.contactEmail)}
            </Text>
          </Collapse>
          <Button type="submit" loading={book.isPending}>
            {t.book}
          </Button>
        </Stack>
      </form>
    </Paper>
  )
}
