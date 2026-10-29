import { ActionIcon, Alert, Anchor, Badge, Button, Checkbox, Divider, Group, Menu, Paper, Select, Skeleton, Stack, Tabs, Text, Textarea, TextInput, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IconArrowDown, IconArrowUp, IconEye, IconPencil, IconPlus, IconTrash } from '@tabler/icons-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useBlocker, useNavigate, useParams } from 'react-router'
import { api, errorMessage, fieldError } from '../../api'
import { ConfirmModal } from '../../components/ConfirmModal'
import { FormFields } from '../../components/FormFields'
import { PageHeader } from '../../components/Page'
import { fieldTypes, formsKey, hasOptions, useForm, type Answers, type FieldType, type Form, type FormField } from '../../forms'
import { defaultLanguage, dictionaries } from '../../locales'
import { notifySuccess } from '../../notify'
import { useSections } from '../../sections'

type Draft = Pick<Form, 'title' | 'description' | 'fields'>
const empty: Draft = { title: '', description: '', fields: [] }

// /admin/forms/new or /admin/forms/:id
export function FormBuilderPage() {
  const { id } = useParams()
  const formId = id === 'new' ? null : Number(id)
  const form = useForm(formId)

  if (form.isError) return <Alert color="red">{errorMessage(form.error)}</Alert>
  if (formId !== null && !form.data) return <Skeleton height={300} />
  return <Builder key={formId ?? 'new'} id={formId} initial={form.data ?? empty} isOpen={!!form.data?.isOpen} />
}

function Builder({ id, initial, isOpen }: { id: number | null; initial: Draft; isOpen: boolean }) {
  const [draft, setDraft] = useState<Draft>({ title: initial.title, description: initial.description ?? '', fields: initial.fields })
  const [savedDraft, setSavedDraft] = useState(draft)
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  // Unsaved edits: ask before leaving the page (in-app links) or closing the tab.
  const dirty = JSON.stringify(draft) !== JSON.stringify(savedDraft)
  const dirtyRef = useRef(dirty)
  useEffect(() => {
    dirtyRef.current = dirty
  }, [dirty])
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirtyRef.current && currentLocation.pathname !== nextLocation.pathname)
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const save = useMutation({
    mutationFn: (d: Draft) => (id === null ? api<Form>('/api/forms', { method: 'POST', json: d }) : api<Form>(`/api/forms/${id}`, { method: 'PUT', json: d })),
    onSuccess: (saved, sent) => {
      notifySuccess('Form saved.')
      setSavedDraft(sent)
      dirtyRef.current = JSON.stringify(draft) !== JSON.stringify(sent) // before the redirect below, which the blocker sees
      queryClient.invalidateQueries({ queryKey: formsKey })
      if (id === null) navigate(`/admin/forms/${saved.id}`, { replace: true })
    },
  })

  const setFields = (fields: FormField[]) => setDraft({ ...draft, fields })
  const update = (i: number, f: FormField) => setFields(draft.fields.map((x, j) => (j === i ? f : x)))
  const move = (i: number, by: number) => {
    const fields = [...draft.fields]
    ;[fields[i], fields[i + by]] = [fields[i + by], fields[i]]
    setFields(fields)
  }
  const add = (type: FieldType) =>
    setFields([...draft.fields, { id: crypto.randomUUID().slice(0, 8), type, label: type === 'Sections' ? 'Which sections are you applying to?' : '', required: type === 'Sections', options: hasOptions(type) ? [''] : [] }])

  const hasSections = draft.fields.some((f) => f.type === 'Sections')
  const fieldErrorsShown = draft.fields.some((_, i) => fieldError(save.error, `Fields[${i}]`)) || !!fieldError(save.error, 'Title')

  return (
    <Stack>
      <PageHeader
        title={id === null ? 'New form' : 'Edit form'}
        description={
          <>
            <Anchor component={Link} to="/admin/forms" size="sm">
              ← All forms
            </Anchor>
            {id !== null && (
              <Badge component="span" ml="sm" variant="light" color={isOpen ? 'signal' : 'gray'}>
                {isOpen ? 'Open' : 'Closed'}
              </Badge>
            )}
          </>
        }
      >
        <Button loading={save.isPending} onClick={() => (save.reset(), save.mutate(draft))}>
          Save
        </Button>
      </PageHeader>

      {!!save.error && !fieldErrorsShown && <Alert color="red">{errorMessage(save.error)}</Alert>}

      <ConfirmModal
        opened={blocker.state === 'blocked'}
        title="Unsaved changes"
        message="You have changes that aren't saved. Leave the page and lose them?"
        confirmLabel="Leave without saving"
        loading={false}
        error={null}
        onConfirm={() => blocker.proceed?.()}
        onClose={() => blocker.reset?.()}
      />

      <Tabs defaultValue="edit" keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="edit" leftSection={<IconPencil size={16} />}>
            Edit
          </Tabs.Tab>
          <Tabs.Tab value="preview" leftSection={<IconEye size={16} />}>
            Preview
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="edit">
          <Stack maw={760}>
            <TextInput label="Title" required value={draft.title} error={fieldError(save.error, 'Title')} onChange={(e) => setDraft({ ...draft, title: e.currentTarget.value })} />
            <Textarea label="Description" description="Shown under the title" autosize minRows={2} value={draft.description ?? ''} onChange={(e) => setDraft({ ...draft, description: e.currentTarget.value })} />

            <Paper withBorder p="sm" bg="var(--mantine-color-default-hover)">
              <Text size="sm" c="dimmed">
                Every form starts with <b>full name</b>, <b>e-mail</b> and the <b>data-processing consent</b> – no need to add them.
              </Text>
            </Paper>

            {draft.fields.map((f, i) => (
              <FieldEditor
                key={f.id}
                field={f}
                error={fieldError(save.error, `Fields[${i}]`)}
                first={i === 0}
                last={i === draft.fields.length - 1}
                onChange={(x) => update(i, x)}
                onMove={(by) => move(i, by)}
                onRemove={() => setFields(draft.fields.filter((_, j) => j !== i))}
              />
            ))}

            <Group>
              <Menu position="bottom-start">
                <Menu.Target>
                  <Button variant="default" leftSection={<IconPlus size={16} />}>
                    Add question
                  </Button>
                </Menu.Target>
                <Menu.Dropdown>
                  {fieldTypes.map((t) => (
                    <Menu.Item key={t.value} disabled={t.value === 'Sections' && hasSections} onClick={() => add(t.value)}>
                      {t.label}
                    </Menu.Item>
                  ))}
                </Menu.Dropdown>
              </Menu>
              {!hasSections && (
                <Text size="sm" c="dimmed">
                  Add a sections question before opening the form – each chosen section becomes an application.
                </Text>
              )}
            </Group>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="preview">
          <Preview draft={draft} />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  )
}

type EditorProps = {
  field: FormField
  error?: string
  first: boolean
  last: boolean
  onChange: (f: FormField) => void
  onMove: (by: number) => void
  onRemove: () => void
}

function FieldEditor({ field: f, error, first, last, onChange, onMove, onRemove }: EditorProps) {
  return (
    <Paper withBorder p="md">
      <Stack gap="sm">
        <Group align="flex-end" wrap="nowrap">
          <TextInput label="Question" required style={{ flex: 1 }} value={f.label} onChange={(e) => onChange({ ...f, label: e.currentTarget.value })} />
          <Select
            label="Type"
            w={170}
            allowDeselect={false}
            data={fieldTypes.filter((t) => t.value !== 'Sections' || f.type === 'Sections')}
            disabled={f.type === 'Sections'}
            value={f.type}
            onChange={(v) => v && onChange({ ...f, type: v as FieldType, options: hasOptions(v as FieldType) && !f.options.length ? [''] : f.options })}
          />
        </Group>
        {hasOptions(f.type) && (
          <Textarea
            label="Options"
            description="One per line"
            autosize
            minRows={2}
            value={f.options.join('\n')}
            onChange={(e) => onChange({ ...f, options: e.currentTarget.value.split('\n') })}
          />
        )}
        {f.type === 'Sections' && (
          <Text size="sm" c="dimmed">
            Recruits tick one or more of the club's sections; each one becomes an application in that section.
          </Text>
        )}
        {error && (
          <Text size="sm" c="red">
            {error}
          </Text>
        )}
        <Group justify="space-between">
          <Checkbox label="Required" checked={f.required} onChange={(e) => onChange({ ...f, required: e.currentTarget.checked })} />
          <Group gap={4}>
            <ActionIcon variant="subtle" color="gray" aria-label="Move up" disabled={first} onClick={() => onMove(-1)}>
              <IconArrowUp size={16} />
            </ActionIcon>
            <ActionIcon variant="subtle" color="gray" aria-label="Move down" disabled={last} onClick={() => onMove(1)}>
              <IconArrowDown size={16} />
            </ActionIcon>
            <ActionIcon variant="subtle" color="red" aria-label="Remove question" onClick={onRemove}>
              <IconTrash size={16} />
            </ActionIcon>
          </Group>
        </Group>
      </Stack>
    </Paper>
  )
}

// What recruits will see (in the default language); answers here go nowhere.
function Preview({ draft }: { draft: Draft }) {
  const sections = useSections()
  const t = dictionaries[defaultLanguage]
  const [answers, setAnswers] = useState<Answers>({})
  return (
    <Paper withBorder p="lg" maw={640}>
      <Stack>
        <Title order={3}>{draft.title || 'Untitled form'}</Title>
        {draft.description && <Text style={{ whiteSpace: 'pre-line' }}>{draft.description}</Text>}
        <TextInput label={t.fullName} required />
        <TextInput label={t.email} required type="email" />
        <FormFields fields={draft.fields} sections={sections.data ?? []} answers={answers} onChange={setAnswers} />
        <Divider />
        <Checkbox required label={t.consent} />
        <Text size="sm" c="dimmed">
          Preview only – nothing is submitted.
        </Text>
      </Stack>
    </Paper>
  )
}
