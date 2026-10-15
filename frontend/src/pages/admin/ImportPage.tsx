import { Alert, Anchor, Badge, Button, Checkbox, FileInput, Group, Loader, MultiSelect, Paper, Select, SimpleGrid, Stack, Table, Text, Textarea, TextInput } from '@mantine/core'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../api'
import { PageHeader } from '../../components/Page'
import { parseTable } from '../../csv'
import { IconCheck } from '@tabler/icons-react'
import { useSections } from '../../sections'

type Status = 'New' | 'AlreadyInvited' | 'Error'
type ResultRow = { lines: number[]; email: string; name: string | null; sections: string[]; language: string; status: Status; message: string | null }
type Result = { rows: ResultRow[]; created: number; emailsQueued: number }

const IGNORE = 'ignore'
const languages = [
  { value: 'pl', label: 'Polski' },
  { value: 'en', label: 'English' },
]
const statusBadge: Record<Status, [string, string]> = { New: ['signal', 'New'], AlreadyInvited: ['gray', 'Already invited'], Error: ['red', 'Error'] }

// Checkbox answers in Google Forms land in one cell: "AI, Web".
const splitAnswers = (cell: string) => cell.split(/[,;]/).map((a) => a.trim()).filter(Boolean)
const norm = (s: string) => s.trim().toLowerCase()
const guess = (headers: string[], ...words: string[]) => {
  const i = headers.findIndex((h) => words.some((w) => norm(h).includes(w)))
  return i >= 0 ? String(i) : null
}

export function ImportPage() {
  const sections = useSections()
  const emailSettings = useQuery({ queryKey: ['email-settings'], queryFn: () => api<{ configured: boolean }>('/api/email/settings') })

  // 1. Source
  const [text, setText] = useState('')
  const [hasHeader, setHasHeader] = useState(true)
  const table = useMemo(() => parseTable(text), [text])
  const headers = hasHeader && table.length ? table[0] : (table[0] ?? []).map((_, i) => `Column ${i + 1}`)
  const data = hasHeader ? table.slice(1) : table
  const firstLine = hasHeader ? 2 : 1
  const columns = headers.map((h, i) => ({ value: String(i), label: h || `Column ${i + 1}` }))

  // 2. Columns (null = not chosen yet → guessed from the header)
  const [nameCols, setNameCols] = useState<string[] | null>(null)
  const [emailCol, setEmailCol] = useState<string | null>(null)
  const [sectionCol, setSectionCol] = useState<string | null>(null)
  const [languageCol, setLanguageCol] = useState<string | null>(null)
  const cols = {
    name: nameCols ?? [guess(headers, 'imię', 'imie', 'nazwisk', 'name')].filter((c): c is string => !!c),
    email: emailCol ?? guess(headers, 'e-mail', 'email', 'mail'),
    section: sectionCol ?? guess(headers, 'sekcj', 'section'),
    language: languageCol ?? guess(headers, 'język', 'jezyk', 'language'),
  }

  // 3. Answer → section / language
  const [sectionMap, setSectionMap] = useState<Record<string, string>>({})
  const [languageMap, setLanguageMap] = useState<Record<string, string>>({})
  const answers = [...new Set(data.flatMap((r) => (cols.section ? splitAnswers(r[+cols.section] ?? '') : [])))].sort()
  const languageValues = [...new Set(data.map((r) => (cols.language ? (r[+cols.language] ?? '').trim() : '')).filter(Boolean))].sort()
  const sectionFor = (answer: string) =>
    sectionMap[answer] ?? String(sections.data?.find((s) => norm(s.name) === norm(answer))?.id ?? IGNORE)
  const languageFor = (value: string) => languageMap[value] ?? (/^(en|eng|ang)/i.test(value) ? 'en' : 'pl')

  // 4. Options
  const [expires, setExpires] = useState('')
  const [send, setSend] = useState(true)
  const sendEmails = send && !!emailSettings.data?.configured

  const rows = data.map((r, i) => ({
    line: i + firstLine,
    name: cols.name.map((c) => r[+c] ?? '').join(' ').replace(/\s+/g, ' ').trim() || null,
    email: cols.email ? (r[+cols.email] ?? '') : '',
    sectionIds: cols.section ? splitAnswers(r[+cols.section] ?? '').map(sectionFor).filter((id) => id !== IGNORE).map(Number) : [],
    language: cols.language ? languageFor((r[+cols.language] ?? '').trim()) : 'pl',
  }))
  const request = (dryRun: boolean) => ({ rows, dryRun, sendEmails, expiresAt: expires ? new Date(`${expires}T23:59:59`).toISOString() : null })

  const preview = useMutation({ mutationFn: () => api<Result>('/api/booking-links/import', { method: 'POST', json: request(true) }) })
  const commit = useMutation({ mutationFn: () => api<Result>('/api/booking-links/import', { method: 'POST', json: request(false) }) })
  const changed = () => (preview.reset(), commit.reset()) // any edit invalidates the preview
  const count = (s: Status) => preview.data?.rows.filter((r) => r.status === s).length ?? 0

  if (commit.data)
    return (
      <Stack maw={720}>
        <PageHeader title="Import recruits" />
        <Alert color="signal" title="Import finished" icon={<IconCheck size={18} />}>
          Created {commit.data.created} personal links
          {commit.data.emailsQueued > 0 ? ` and queued ${commit.data.emailsQueued} invitation e-mails – they go out over the next minutes.` : '. No e-mails were sent.'}
        </Alert>
        <Group>
          <Button component={Link} to="/admin/links">
            Booking links
          </Button>
          {commit.data.emailsQueued > 0 && (
            <Button component={Link} to="/admin/email" variant="default">
              E-mail status
            </Button>
          )}
          <Button variant="subtle" onClick={() => (setText(''), changed())}>
            Import more
          </Button>
        </Group>
      </Stack>
    )

  return (
    <Stack>
      <PageHeader
        title="Import recruits"
        description="Everyone gets their own booking link for the sections they applied to, by e-mail. Importing the same list again skips people who already have a link."
      />

      <Paper withBorder p="md">
        <Stack>
          <Text fw={600}>1. Paste the responses or upload a CSV</Text>
          <Text size="sm" c="dimmed">
            Google Forms → Responses → <i>Link to Sheets</i>, then select all and copy, or <i>Download responses (.csv)</i>.
          </Text>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Textarea placeholder="Paste here…" autosize minRows={3} maxRows={8} value={text} onChange={(e) => (setText(e.currentTarget.value), changed())} />
            <FileInput
              placeholder="…or choose a .csv file"
              accept=".csv,text/csv"
              clearable
              onChange={async (f) => (setText(f ? await f.text() : ''), changed())}
            />
          </SimpleGrid>
          <Checkbox label="First row contains column names" checked={hasHeader} onChange={(e) => (setHasHeader(e.currentTarget.checked), changed())} />
          {data.length > 0 && <Text size="sm">{data.length} rows found.</Text>}
        </Stack>
      </Paper>

      {data.length > 0 && (
        <Paper withBorder p="md">
          <Stack>
            <Text fw={600}>2. Which column is what?</Text>
            <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
              <MultiSelect label="Name" description="Several columns are joined" data={columns} value={cols.name} onChange={(v) => (setNameCols(v), changed())} />
              <Select label="E-mail" required data={columns} value={cols.email} onChange={(v) => (setEmailCol(v), changed())} />
              <Select label="Sections" required data={columns} value={cols.section} onChange={(v) => (setSectionCol(v), changed())} />
              <Select label="Language" description="Optional – default Polish" clearable data={columns} value={cols.language} onChange={(v) => (setLanguageCol(v ?? ''), changed())} />
            </SimpleGrid>
          </Stack>
        </Paper>
      )}

      {answers.length > 0 && (
        <Paper withBorder p="md">
          <Stack>
            <Text fw={600}>3. Form answers → sections</Text>
            <Table>
              <Table.Tbody>
                {answers.map((a) => (
                  <Table.Tr key={a}>
                    <Table.Td>{a}</Table.Td>
                    <Table.Td w={260}>
                      <Select
                        data={[...(sections.data ?? []).map((s) => ({ value: String(s.id), label: s.name })), { value: IGNORE, label: '— ignore —' }]}
                        value={sectionFor(a)}
                        onChange={(v) => (setSectionMap({ ...sectionMap, [a]: v ?? IGNORE }), changed())}
                        allowDeselect={false}
                        error={sectionFor(a) === IGNORE && 'not mapped'}
                      />
                    </Table.Td>
                  </Table.Tr>
                ))}
                {languageValues.map((v) => (
                  <Table.Tr key={`lang-${v}`}>
                    <Table.Td>
                      Language: <i>{v}</i>
                    </Table.Td>
                    <Table.Td>
                      <Select data={languages} value={languageFor(v)} allowDeselect={false} onChange={(l) => (setLanguageMap({ ...languageMap, [v]: l ?? 'pl' }), changed())} />
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Stack>
        </Paper>
      )}

      {data.length > 0 && cols.email && cols.section && (
        <Paper withBorder p="md">
          <Stack>
            <Text fw={600}>4. Check and send</Text>
            <Group align="flex-end">
              <TextInput type="date" label="Links expire (optional)" value={expires} onChange={(e) => (setExpires(e.currentTarget.value), changed())} />
              <Checkbox
                label="Send invitation e-mails"
                checked={sendEmails}
                disabled={!emailSettings.data?.configured}
                onChange={(e) => (setSend(e.currentTarget.checked), changed())}
                description={
                  emailSettings.data && !emailSettings.data.configured ? (
                    <>
                      E-mail isn't configured – links only (<Anchor component={Link} to="/admin/email" size="xs">set up</Anchor>)
                    </>
                  ) : (
                    <>
                      Text: <Anchor component={Link} to="/admin/email" size="xs">E-mail → Invitation</Anchor>
                    </>
                  )
                }
              />
              <Button variant="default" loading={preview.isPending} onClick={() => preview.mutate()}>
                Preview
              </Button>
            </Group>
            {preview.isError && <Alert color="red">{errorMessage(preview.error)}</Alert>}

            {preview.data && (
              <>
                <Group gap="xs">
                  <Badge color="signal">{count('New')} new</Badge>
                  <Badge color="gray">{count('AlreadyInvited')} already invited</Badge>
                  <Badge color="red">{count('Error')} with errors</Badge>
                </Group>
                <Table.ScrollContainer minWidth={640} mah={420}>
                  <Table striped stickyHeader>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Row</Table.Th>
                        <Table.Th>Recruit</Table.Th>
                        <Table.Th>Sections</Table.Th>
                        <Table.Th>Lang.</Table.Th>
                        <Table.Th>Status</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {preview.data.rows.map((r) => (
                        <Table.Tr key={r.lines.join()}>
                          <Table.Td>{r.lines.join(', ')}</Table.Td>
                          <Table.Td>
                            <Text size="sm">{r.name ?? '—'}</Text>
                            <Text size="xs" c="dimmed">
                              {r.email || '(no e-mail)'}
                            </Text>
                          </Table.Td>
                          <Table.Td>{r.sections.join(', ') || '—'}</Table.Td>
                          <Table.Td>{r.language}</Table.Td>
                          <Table.Td>
                            <Badge color={statusBadge[r.status][0]} variant="light">
                              {statusBadge[r.status][1]}
                            </Badge>
                            {r.message && (
                              <Text size="xs" c={r.status === 'Error' ? 'red' : 'dimmed'}>
                                {r.message}
                              </Text>
                            )}
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </Table.ScrollContainer>
                {commit.isError && <Alert color="red">{errorMessage(commit.error)}</Alert>}
                <Group justify="space-between">
                  <Text size="sm" c="dimmed">
                    {count('Error') > 0 && 'Rows with errors are skipped – fix them in the sheet and import again later. '}
                  </Text>
                  <Button disabled={count('New') === 0} loading={commit.isPending} onClick={() => commit.mutate()}>
                    {sendEmails ? `Create ${count('New')} links & send ${count('New')} e-mails` : `Create ${count('New')} links`}
                  </Button>
                </Group>
              </>
            )}
            {preview.isPending && <Loader size="sm" />}
          </Stack>
        </Paper>
      )}
    </Stack>
  )
}
