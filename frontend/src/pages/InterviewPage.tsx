import { Alert, Anchor, Paper, SimpleGrid, Stack, Text, Textarea, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, errorMessage } from '../api'
import { isAdmin, useMe } from '../auth'
import { PageHeader, TableSkeleton } from '../components/Page'
import { PrepPanel } from '../components/PrepPanel'
import { interviewsKey, notesKey, type Interview, type Note, type Notes, type Prep } from '../interviews'
import { InterviewCard } from './InterviewsPage'

const when = (iso: string) => new Date(iso).toLocaleString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export function InterviewPage() {
  const id = Number(useParams().id)
  const me = useMe()
  const interview = useQuery({ queryKey: [...interviewsKey, id], queryFn: () => api<Interview>(`/api/interviews/${id}`) })
  const notes = useQuery({ queryKey: notesKey(id), queryFn: () => api<Notes>(`/api/interviews/${id}/notes`), refetchOnWindowFocus: false })
  const prep = useQuery({ queryKey: [...interviewsKey, id, 'prep'], queryFn: () => api<Prep>(`/api/interviews/${id}/prep`) })
  const myId = me.data?.id
  const others = notes.data?.notes.filter((n) => n.authorId !== myId) ?? []

  return (
    <Stack>
      <Anchor component={Link} to="/interviews" size="sm">
        ← Interviews
      </Anchor>
      <PageHeader title={interview.data ? interview.data.recruit.fullName : 'Interview'} description={interview.data && when(interview.data.start)} />
      {interview.isError && <Alert color="red">{errorMessage(interview.error)}</Alert>}
      {interview.data && <InterviewCard interview={interview.data} admin={isAdmin(me.data)} />}

      {/* Prep next to the notes: read the answers and questions while writing. */}
      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="xl" mt="md">
        <div>{prep.data ? <PrepPanel prep={prep.data} /> : prep.isPending && <TableSkeleton rows={3} />}</div>
        <Stack>
          <Title order={3}>
            Notes
          </Title>
          {notes.isPending && <TableSkeleton rows={2} />}
          {notes.isError && !interview.isError && <Alert color="red">{errorMessage(notes.error)}</Alert>}
          {notes.data?.canWrite && (
            <MyNote key={id} interviewId={id} initial={notes.data.notes.find((n) => n.authorId === myId)?.text ?? ''} />
          )}
          {notes.data && !notes.data.canWrite && others.length === 0 && (
            <Text c="dimmed" size="sm">
              No notes yet.
            </Text>
          )}
          {others.map((n) => (
            <OtherNote key={`${n.authorId}-${n.authorName}`} note={n} />
          ))}
        </Stack>
      </SimpleGrid>
    </Stack>
  )
}

export function OtherNote({ note }: { note: Note }) {
  return (
    <Paper withBorder p="sm">
      <Text size="sm" fw={600}>
        {note.authorName}{' '}
        <Text span size="xs" c="dimmed" fw={400}>
          · {when(note.updatedAt)}
        </Text>
      </Text>
      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>
        {note.text}
      </Text>
    </Paper>
  )
}

// Autosaves ~1 s after typing stops. Mutations share a scope, so saves run in order and the last text wins.
function MyNote({ interviewId, initial }: { interviewId: number; initial: string }) {
  const queryClient = useQueryClient()
  const [text, setText] = useState(initial)
  const [savedText, setSavedText] = useState(initial.trim())
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [debounced] = useDebouncedValue(text, 1000)
  const save = useMutation({
    scope: { id: `note-${interviewId}` },
    mutationFn: (t: string) => api<Note | undefined>(`/api/interviews/${interviewId}/notes`, { method: 'PUT', json: { text: t } }),
    onSuccess: (_, t) => {
      setSavedText(t.trim())
      setSavedAt(new Date().toISOString())
      queryClient.invalidateQueries({ queryKey: notesKey(interviewId) }) // others' notes may have changed too
    },
  })
  const { mutate } = save
  const dirty = text.trim() !== savedText

  useEffect(() => {
    if (debounced.trim() !== savedText) mutate(debounced)
  }, [debounced, savedText, mutate])

  // Closing the tab within the debounce window would lose the last words.
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const status = save.isError
    ? undefined
    : save.isPending || dirty
      ? 'Saving…'
      : savedAt
        ? `Saved at ${new Date(savedAt).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })}`
        : 'Saved automatically as you type.'

  return (
    <Textarea
      label="Your note"
      description={status}
      error={save.isError ? `Couldn't save: ${errorMessage(save.error)}. Keep typing to retry.` : undefined}
      placeholder="How did it go? Strengths, weaknesses, which section fits…"
      value={text}
      onChange={(e) => setText(e.currentTarget.value)}
      onBlur={() => dirty && mutate(text)}
      autosize
      minRows={5}
      maxLength={20000}
    />
  )
}
