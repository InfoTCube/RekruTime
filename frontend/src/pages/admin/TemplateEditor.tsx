import { Alert, Badge, Button, Code, Group, Loader, Paper, SegmentedControl, Skeleton, SimpleGrid, Stack, Tabs, Text, Textarea, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../../api'
import { notifySuccess } from '../../notify'

type Template = { key: string; language: string; subject: string; body: string; isDefault: boolean }
type Rendered = { subject: string; body: string; html: string }

const languageNames: Record<string, string> = { pl: 'Polski', en: 'English' }
const common: [string, string][] = [
  ['{firstName}', 'first name'],
  ['{name}', 'full name'],
]
// Every e-mail admins can edit (keys match the API's EmailTemplates).
const kinds: Record<string, { label: string; title: string; when: string; placeholders: [string, string][] }> = {
  invitation: {
    label: 'Invitation',
    title: 'Invitation e-mail',
    when: "Sent with each personal booking link, in the recruit's language.",
    placeholders: [...common, ['{link}', 'personal booking link'], ['{sections}', 'their sections'], ['{deadline}', 'last interview day'], ['{contact}', 'contact e-mail']],
  },
  accepted: {
    label: 'Accepted',
    title: 'Decision: accepted',
    when: 'Sent when an admin sends the decision and at least one application was accepted.',
    placeholders: [...common, ['{sections}', 'accepted sections'], ['{contact}', 'contact e-mail']],
  },
  rejected: {
    label: 'Rejected',
    title: 'Decision: rejected',
    when: 'Sent when an admin sends the decision and every application was rejected.',
    placeholders: [...common, ['{sections}', 'sections they applied to'], ['{contact}', 'contact e-mail']],
  },
}

export function TemplateEditor() {
  const templates = useQuery({ queryKey: ['email-templates'], queryFn: () => api<Template[]>('/api/email/templates') })
  const [key, setKey] = useState('invitation')
  if (templates.isPending) return <Skeleton height={320} />
  if (templates.isError) return <Alert color="red">{errorMessage(templates.error)}</Alert>
  const kind = kinds[key]
  const list = templates.data.filter((t) => t.key === key)
  const placeholders = kind.placeholders

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={4}>{kind.title}</Title>
        <SegmentedControl value={key} onChange={setKey} data={Object.entries(kinds).map(([value, k]) => ({ value, label: k.label }))} />
      </Group>
      <Text size="sm" c="dimmed">
        {kind.when} Placeholders:{' '}
        {placeholders.map(([p, what], i) => (
          <span key={p}>
            <Code>{p}</Code> {what}
            {i < placeholders.length - 1 && ', '}
          </span>
        ))}
        .
      </Text>
      <Tabs key={key} defaultValue={list[0]?.language}>
        <Tabs.List>
          {list.map((t) => (
            <Tabs.Tab key={t.language} value={t.language} rightSection={!t.isDefault && <Badge size="xs">edited</Badge>}>
              {languageNames[t.language] ?? t.language}
            </Tabs.Tab>
          ))}
        </Tabs.List>
        {list.map((t) => (
          <Tabs.Panel key={t.language} value={t.language} pt="md">
            <TemplateForm key={`${t.subject}|${t.body}`} template={t} />
          </Tabs.Panel>
        ))}
      </Tabs>
    </Stack>
  )
}

function TemplateForm({ template }: { template: Template }) {
  const queryClient = useQueryClient()
  const [subject, setSubject] = useState(template.subject)
  const [body, setBody] = useState(template.body)
  const [debounced] = useDebouncedValue({ subject, body }, 400)
  const url = `/api/email/templates/${template.key}/${template.language}`

  const preview = useQuery({
    queryKey: ['email-template-preview', template.key, template.language, debounced],
    queryFn: () => api<Rendered>(`${url}/preview`, { method: 'POST', json: debounced }),
    enabled: !!debounced.subject && !!debounced.body,
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['email-templates'] })
  const save = useMutation({
    mutationFn: () => api(url, { method: 'PUT', json: { subject, body } }),
    onSuccess: () => (notifySuccess('E-mail text saved.'), refresh()),
  })
  const reset = useMutation({ mutationFn: () => api(url, { method: 'DELETE' }), onSuccess: refresh })
  const changed = subject !== template.subject || body !== template.body

  return (
    <SimpleGrid cols={{ base: 1, md: 2 }}>
      <Stack>
        {(save.isError || reset.isError) && !fieldError(save.error, 'Body') && <Alert color="red">{errorMessage(save.error ?? reset.error)}</Alert>}
        <TextInput label="Subject" value={subject} onChange={(e) => setSubject(e.currentTarget.value)} error={fieldError(save.error, 'Subject')} />
        <Textarea label="Text" autosize minRows={12} value={body} onChange={(e) => setBody(e.currentTarget.value)} error={fieldError(save.error, 'Body')} />
        <Group justify="space-between">
          {!template.isDefault ? (
            <Button variant="subtle" color="gray" loading={reset.isPending} onClick={() => reset.mutate()}>
              Reset to default
            </Button>
          ) : (
            <span />
          )}
          <Button disabled={!changed} loading={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </Group>
      </Stack>
      <Paper withBorder p="md" bg="var(--mantine-color-default-hover)">
        <Text size="xs" c="dimmed" mb="xs">
          Preview (sample recruit) – sent as HTML, with this text as the plain-text version
        </Text>
        {preview.data ? (
          <>
            <Text fw={600} mb="sm">
              {preview.data.subject}
            </Text>
            {/* sandboxed: no scripts; same-origin only to size the frame to its content */}
            <iframe
              title="E-mail preview"
              srcDoc={preview.data.html}
              sandbox="allow-same-origin"
              style={{ width: '100%', border: 0, borderRadius: 'var(--mantine-radius-sm)' }}
              onLoad={(e) => (e.currentTarget.style.height = `${e.currentTarget.contentDocument?.documentElement.scrollHeight ?? 600}px`)}
            />
          </>
        ) : (
          preview.isFetching && <Loader size="sm" />
        )}
      </Paper>
    </SimpleGrid>
  )
}
