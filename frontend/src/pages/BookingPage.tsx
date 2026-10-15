import { Alert, Anchor, Badge, Box, Button, Checkbox, Collapse, Container, Group, Paper, ScrollArea, SegmentedControl, SimpleGrid, Skeleton, Stack, Switch, Text, TextInput, ThemeIcon, Title } from '@mantine/core'
import { IconCheck, IconClock, IconMapPin } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError, fieldError } from '../api'
import { LogoMark } from '../components/Logo'
import { SignalTrace } from '../components/SignalTrace'
import { defaultLanguage, dictionaries, type Dictionary } from '../locales'
import { slotColor } from '../theme'

type LinkInfo = {
  clubName: string | null
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
    weekday: (iso: string) => new Date(iso).toLocaleDateString(locale, { weekday: 'short' }),
    dayOfMonth: (iso: string) => new Date(iso).getDate(),
    month: (iso: string) => new Date(iso).toLocaleDateString(locale, { month: 'short' }),
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

  const club = info.data?.clubName
  return (
    <T.Provider value={t}>
      {/* Hero band: who's inviting, what for – and the one animated moment, the signal trace. */}
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
              onChange={choose}
              color="signal.8"
              styles={{ root: { background: 'rgba(255,255,255,0.1)' }, label: { color: 'white' } }}
              data={Object.entries(dictionaries).map(([value, d]) => ({ value, label: value.toUpperCase(), title: d.name }))}
            />
          </Group>
          <Title order={1} c="white" fz={{ base: 30, sm: 40 }} lh={1.1} maw={520}>
            {t.title}
          </Title>
          {info.data && <Meta info={info.data} />}
        </Container>
        <Box pt="md">
          <SignalTrace height={44} />
        </Box>
      </Box>

      <Container size="sm" py="xl" px="md">
        <Stack gap="xl">
          {info.isPending && <Skeleton height={200} />}
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

// Sections, length and place – under the title, on the dark band.
function Meta({ info }: { info: LinkInfo }) {
  const t = useContext(T)
  return (
    <Group gap="md" mt="md">
      <Group gap={6}>
        {info.sections.map((s) => (
          <Badge key={s} size="lg" variant="filled" color="signal.8" tt="none">
            {s}
          </Badge>
        ))}
      </Group>
      <Group gap={4} c="cortex.1">
        <IconClock size={16} aria-hidden />
        <Text size="sm">{t.minutes(info.durationMinutes)}</Text>
      </Group>
      {info.location && (
        <Group gap={4} c="cortex.1" wrap="nowrap">
          <IconMapPin size={16} aria-hidden />
          <Text size="sm">{info.location}</Text>
        </Group>
      )}
    </Group>
  )
}

// Numbered because booking really is a sequence: day → time → details.
function Step({ n, title, children }: { n: number; title: string; children: React.ReactNode }) {
  return (
    <Stack gap="sm">
      <Group gap="sm">
        <ThemeIcon radius="xl" size={28} color="cortex" variant="light" aria-hidden>
          <Text fw={700} size="sm">
            {n}
          </Text>
        </ThemeIcon>
        <Title order={3} fz="lg">
          {title}
        </Title>
      </Group>
      {children}
    </Stack>
  )
}

function Booked({ info }: { info: LinkInfo }) {
  const t = useContext(T)
  const f = useFormat()
  const b = info.booking!
  return (
    <Paper withBorder p="xl" shadow="sm">
      <Stack align="center" ta="center" gap="sm">
        <ThemeIcon size={56} radius="xl" color="signal.7" aria-hidden>
          <IconCheck size={32} stroke={2.5} />
        </ThemeIcon>
        <Title order={2} fz={26}>
          {t.booked}
        </Title>
        <Stack gap={0}>
          <Text fz="lg" fw={500}>
            {f.longDay(b.start)}
          </Text>
          <Text fz={28} fw={700} ff="heading" style={{ whiteSpace: 'nowrap' }}>
            {f.time(b.start)}–{f.time(b.end)}
          </Text>
        </Stack>
        {info.location && <Text c="dimmed">{info.location}</Text>}
        <Text size="sm">{t.bookedNote}</Text>
        <Contact info={info}>{t.needChange}</Contact>
      </Stack>
    </Paper>
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
  const yellowSwitch = (
    <Switch
      color="spark.6"
      label={t.noneSuit}
      checked={showYellow}
      onChange={(e) => {
        setShowYellow(e.currentTarget.checked)
        setPicked(null)
      }}
    />
  )

  return (
    <Stack gap="xl">
      <Text>{t.pickTime}</Text>

      {slots.isPending && <Skeleton height={140} />}
      {slots.isError && <Alert color="red">{t.somethingWrong}</Alert>}
      {slots.data?.length === 0 && (
        <Stack gap="sm">
          <Alert color="cortex">{showYellow ? t.noTimes : t.noGreenTimes}</Alert>
          {!showYellow && yellowSwitch}
        </Stack>
      )}

      {days.length > 0 && (
        <Step n={1} title={t.stepDay}>
          <ScrollArea type="auto" offsetScrollbars>
            <Group gap="xs" wrap="nowrap" pb={4} role="radiogroup" aria-label={t.stepDay}>
              {days.map((d) => {
                const iso = `${d}T12:00:00`
                const active = d === selectedDay
                return (
                  <Button
                    key={d}
                    role="radio"
                    aria-checked={active}
                    aria-label={f.longDay(iso)}
                    variant={active ? 'filled' : 'default'}
                    h="auto"
                    w={64}
                    py={8}
                    px={0}
                    onClick={() => {
                      setDay(d)
                      setPicked(null)
                    }}
                  >
                    <Stack gap={0} align="center">
                      <Text fz="xs" fw={500} inherit>
                        {f.weekday(iso)}
                      </Text>
                      <Text fz={22} fw={700} ff="heading" lh={1.2} inherit>
                        {f.dayOfMonth(iso)}
                      </Text>
                      <Text fz="xs" inherit>
                        {f.month(iso)}
                      </Text>
                    </Stack>
                  </Button>
                )
              })}
            </Group>
          </ScrollArea>
        </Step>
      )}
      {daySlots.length > 0 && (
        <Step n={2} title={t.stepTime}>
          <SimpleGrid cols={{ base: 3, xs: 4, sm: 6 }} spacing="xs">
            {daySlots.map((s) => {
              const active = picked?.start === s.start
              return (
                <Button
                  key={s.start}
                  aria-pressed={active}
                  variant={active ? 'filled' : 'light'}
                  color={active ? `${slotColor[s.color]}.8` : slotColor[s.color]}
                  c={active ? undefined : `var(--slot-${s.color})`}
                  onClick={() => setPicked(s)}
                >
                  {f.time(s.start)}
                </Button>
              )
            })}
          </SimpleGrid>
          {yellowSwitch}
          {showYellow && (
            <Stack gap={4}>
              <Text size="sm">{t.yellowExplained}</Text>
              <Contact info={info}>{t.contactIfNone}</Contact>
            </Stack>
          )}
        </Step>
      )}

      {picked && (
        <Step n={3} title={t.stepDetails}>
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
        </Step>
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
    <Paper withBorder p="lg" shadow="sm">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          book.mutate()
        }}
      >
        <Stack>
          <Text fw={600} ff="heading" fz="lg">
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
