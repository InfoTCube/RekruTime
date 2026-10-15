import { ActionIcon, Alert, Button, Group, Loader, Modal, NumberInput, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../../api'
import { ConfirmModal } from '../../components/ConfirmModal'
import { sectionsKey, useSections, type Section } from '../../sections'

type SectionForm = Omit<Section, 'id'>
const empty: SectionForm = { name: '', interviewDurationMinutes: 30, bufferMinutes: 0 }

export function SectionsPage() {
  const sections = useSections()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Section | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Section | null>(null)

  const save = useMutation({
    mutationFn: (form: SectionForm) =>
      editing === 'new'
        ? api('/api/sections', { method: 'POST', json: form })
        : api(`/api/sections/${(editing as Section).id}`, { method: 'PUT', json: form }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sectionsKey })
      setEditing(null)
    },
  })

  const remove = useMutation({
    mutationFn: (s: Section) => api(`/api/sections/${s.id}`, { method: 'DELETE' }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sectionsKey })
      setDeleting(null)
    },
  })

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>Sections</Title>
        <Button onClick={() => (save.reset(), setEditing('new'))}>Add section</Button>
      </Group>

      {sections.isPending && <Loader />}
      {sections.isError && <Alert color="red">{errorMessage(sections.error)}</Alert>}
      {sections.data?.length === 0 && <Text c="dimmed">No sections yet.</Text>}
      {!!sections.data?.length && (
        <Table.ScrollContainer minWidth={420}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Interview</Table.Th>
                <Table.Th>Buffer</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {sections.data.map((s) => (
                <Table.Tr key={s.id}>
                  <Table.Td>{s.name}</Table.Td>
                  <Table.Td>{s.interviewDurationMinutes} min</Table.Td>
                  <Table.Td>{s.bufferMinutes ? `${s.bufferMinutes} min` : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end" wrap="nowrap">
                      <Button size="xs" variant="subtle" onClick={() => (save.reset(), setEditing(s))}>
                        Edit
                      </Button>
                      <ActionIcon variant="subtle" color="red" aria-label={`Delete ${s.name}`} onClick={() => (remove.reset(), setDeleting(s))}>
                        ✕
                      </ActionIcon>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'Add section' : 'Edit section'} centered>
        {editing !== null && (
          <SectionFormView
            key={editing === 'new' ? 'new' : editing.id}
            initial={editing === 'new' ? empty : editing}
            error={save.error}
            saving={save.isPending}
            onSubmit={(form) => save.mutate(form)}
          />
        )}
      </Modal>

      <ConfirmModal
        opened={deleting !== null}
        title="Delete section"
        message={
          <>
            Delete <b>{deleting?.name}</b>? People assigned to it lose the assignment.
          </>
        }
        confirmLabel="Delete"
        loading={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </Stack>
  )
}

function SectionFormView(props: { initial: SectionForm; error: unknown; saving: boolean; onSubmit: (f: SectionForm) => void }) {
  const [form, setForm] = useState<SectionForm>({
    name: props.initial.name,
    interviewDurationMinutes: props.initial.interviewDurationMinutes,
    bufferMinutes: props.initial.bufferMinutes,
  })
  const fieldErrorsShown = ['Name', 'InterviewDurationMinutes', 'BufferMinutes'].some((f) => fieldError(props.error, f))

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        props.onSubmit(form)
      }}
    >
      <Stack>
        {!!props.error && !fieldErrorsShown && <Alert color="red">{errorMessage(props.error)}</Alert>}
        <TextInput label="Name" required data-autofocus value={form.name} error={fieldError(props.error, 'Name')} onChange={(e) => setForm({ ...form, name: e.currentTarget.value })} />
        <NumberInput
          label="Interview length (minutes)"
          min={5}
          max={240}
          step={5}
          required
          value={form.interviewDurationMinutes}
          error={fieldError(props.error, 'InterviewDurationMinutes')}
          onChange={(v) => setForm({ ...form, interviewDurationMinutes: Number(v) || 0 })}
        />
        <NumberInput
          label="Buffer after interview (minutes)"
          description="Room time for notes before the next interview"
          min={0}
          max={120}
          step={5}
          value={form.bufferMinutes}
          error={fieldError(props.error, 'BufferMinutes')}
          onChange={(v) => setForm({ ...form, bufferMinutes: Number(v) || 0 })}
        />
        <Group justify="flex-end">
          <Button type="submit" loading={props.saving}>
            Save
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
