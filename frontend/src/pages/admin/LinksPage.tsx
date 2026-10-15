import { Alert, Badge, Button, Checkbox, CopyButton, Group, Modal, MultiSelect, NumberInput, SegmentedControl, Select, Stack, Table, Text, Textarea, TextInput, Tooltip } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../../api'
import { ConfirmModal } from '../../components/ConfirmModal'
import { EmptyState, PageHeader, TableSkeleton } from '../../components/Page'
import { notifySuccess } from '../../notify'
import { IconLink, IconPlus } from '@tabler/icons-react'
import { useSections } from '../../sections'
import { LinkCoverage } from './LinkCoverage'

type Status = 'Unused' | 'Booked' | 'Expired' | 'Inactive'
type InvitationState = 'NotSent' | 'Queued' | 'Sent' | 'Failed'
type Link = {
  id: number
  token: string
  url: string
  label: string | null
  recruitName: string | null
  recruitEmail: string | null
  sections: string[]
  createdAt: string
  expiresAt: string | null
  status: Status
  interviewStart: string | null
  invitation: { state: InvitationState; at: string | null; error: string | null }
}

const linksKey = ['booking-links']
const statusColor: Record<Status, string> = { Unused: 'cortex', Booked: 'signal', Expired: 'gray', Inactive: 'red' }
const fullUrl = (l: Link) => window.location.origin + l.url
const when = (iso: string) => new Date(iso).toLocaleString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })
const who = (l: Link) => l.label ?? l.recruitEmail ?? 'Anonymous'
// Invitations go to the recruit's e-mail, only while the link can still be used.
const canInvite = (l: Link) => !!l.recruitEmail && l.status === 'Unused'
const invitationBadge: Record<InvitationState, [string, string]> = {
  NotSent: ['gray', 'Not sent'],
  Queued: ['cortex', 'Sending…'],
  Sent: ['signal', 'Sent'],
  Failed: ['red', 'Failed'],
}

// "Name, email" / "email" / "Name" per line; comma, semicolon or tab separated (pasting from a spreadsheet works).
function parseRecipients(text: string) {
  return text
    .split('\n')
    .map((line) => line.split(/[,;\t]/).map((p) => p.trim()).filter(Boolean))
    .filter((parts) => parts.length > 0)
    .map((parts) => ({ name: parts.filter((p) => !p.includes('@')).join(' ') || null, email: parts.find((p) => p.includes('@')) ?? null }))
}

function toCsv(links: Link[]) {
  const cell = (v: string | null) => `"${(v ?? '').replaceAll('"', '""')}"`
  const rows = links.map((l) => [l.label, l.recruitEmail, l.sections.join(' + '), fullUrl(l), l.status].map(cell).join(','))
  return ['name,email,sections,link,status', ...rows].join('\n')
}

function downloadCsv(links: Link[]) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob(['﻿' + toCsv(links)], { type: 'text/csv' })) // BOM → Excel reads UTF-8 (Polish letters)
  a.download = `booking-links-${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(a.href)
}

const copyText = (links: Link[]) => links.map((l) => `${who(l)}\t${fullUrl(l)}`).join('\n')

export function LinksPage() {
  const sections = useSections()
  const queryClient = useQueryClient()
  const [sectionId, setSectionId] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [invitation, setInvitation] = useState<string | null>(null)
  const params = new URLSearchParams()
  if (sectionId) params.set('sectionId', sectionId)
  if (status) params.set('status', status)
  if (invitation) params.set('invitation', invitation)
  const links = useQuery({
    queryKey: [...linksKey, sectionId, status, invitation],
    queryFn: () => api<Link[]>(`/api/booking-links?${params}`),
    refetchInterval: (q) => (q.state.data?.some((l) => l.invitation.state === 'Queued') ? 5000 : false), // watch e-mails go out
  })

  const [selected, setSelected] = useState<number[]>([])
  const invitable = (links.data ?? []).filter(canInvite)
  const [toInvite, setToInvite] = useState<number[] | null>(null)
  const invite = useMutation({
    mutationFn: (ids: number[]) => api<{ queued: number; skipped: number }>('/api/booking-links/invite', { method: 'POST', json: { linkIds: ids } }),
    onSuccess: (r) => {
      notifySuccess(
        `${r.queued} invitation${r.queued === 1 ? '' : 's'} queued${r.skipped > 0 ? ` · ${r.skipped} skipped (booked, inactive, expired or without e-mail)` : ''}.`,
      )
      queryClient.invalidateQueries({ queryKey: linksKey })
      setSelected([])
      setToInvite(null)
    },
  })

  const setActive = useMutation({
    mutationFn: (l: Link) => api(`/api/booking-links/${l.id}`, { method: 'PATCH', json: { isActive: l.status === 'Inactive' } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: linksKey }),
  })

  const [creating, setCreating] = useState(false)
  const [created, setCreated] = useState<Link[] | null>(null)
  const [coverageFor, setCoverageFor] = useState<Link | null>(null)

  return (
    <Stack>
      <PageHeader title="Booking links" description="Each link is for one recruit and can book one interview. Send links by e-mail or message.">
        <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>
          Create links
        </Button>
      </PageHeader>

      <Group align="flex-end">
        <Select label="Section" placeholder="All" clearable value={sectionId} onChange={setSectionId} data={(sections.data ?? []).map((s) => ({ value: String(s.id), label: s.name }))} />
        <Select label="Status" placeholder="All" clearable value={status} onChange={setStatus} data={['Unused', 'Booked', 'Expired', 'Inactive']} />
        <Select
          label="Invitation"
          placeholder="All"
          clearable
          value={invitation}
          onChange={setInvitation}
          data={Object.entries(invitationBadge).map(([value, [, label]]) => ({ value, label }))}
        />
        {selected.length > 0 && (
          <Button
            onClick={() => {
              invite.reset()
              setToInvite(selected)
            }}
          >
            Send invitations ({selected.length})
          </Button>
        )}
        {!!links.data?.length && (
          <>
            <CopyButton value={copyText(links.data)}>
              {({ copied, copy }) => (
                <Button variant="default" onClick={copy}>
                  {copied ? 'Copied' : `Copy ${links.data.length} links`}
                </Button>
              )}
            </CopyButton>
            <Button variant="default" onClick={() => downloadCsv(links.data!)}>
              Export CSV
            </Button>
          </>
        )}
      </Group>

      {links.isPending && <TableSkeleton />}
      {links.isError && <Alert color="red">{errorMessage(links.error)}</Alert>}
      {setActive.isError && <Alert color="red">{errorMessage(setActive.error)}</Alert>}
      {links.data?.length === 0 &&
        (sectionId || status || invitation ? (
          <Text c="dimmed">No links match these filters.</Text>
        ) : (
          <EmptyState icon={<IconLink size={24} />} title="No booking links yet">
            Import your recruits from a form export, or create links by hand with “Create links”.
          </EmptyState>
        ))}
      {!!links.data?.length && (
        <Table.ScrollContainer minWidth={900}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={36}>
                  <Checkbox
                    aria-label="Select all that can be invited"
                    disabled={invitable.length === 0}
                    checked={invitable.length > 0 && invitable.every((l) => selected.includes(l.id))}
                    indeterminate={selected.length > 0 && !invitable.every((l) => selected.includes(l.id))}
                    onChange={(e) => setSelected(e.currentTarget.checked ? invitable.map((l) => l.id) : [])}
                  />
                </Table.Th>
                <Table.Th>Recruit</Table.Th>
                <Table.Th>Sections</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Invitation</Table.Th>
                <Table.Th>Expires</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {links.data.map((l) => (
                <Table.Tr key={l.id}>
                  <Table.Td>
                    <Checkbox
                      aria-label={`Select ${who(l)}`}
                      disabled={!canInvite(l)}
                      checked={selected.includes(l.id)}
                      onChange={(e) => setSelected(e.currentTarget.checked ? [...selected, l.id] : selected.filter((id) => id !== l.id))}
                    />
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{who(l)}</Text>
                    {l.recruitEmail && l.label !== l.recruitEmail && (
                      <Text size="xs" c="dimmed">
                        {l.recruitEmail}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {l.sections.map((s) => (
                        <Badge key={s} variant="outline" size="sm">
                          {s}
                        </Badge>
                      ))}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={statusColor[l.status]} variant="light">
                      {l.status}
                    </Badge>
                    {l.interviewStart && (
                      <Text size="xs" c="dimmed">
                        {when(l.interviewStart)}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {l.recruitEmail ? (
                      <Tooltip label={l.invitation.error} disabled={!l.invitation.error} multiline w={320}>
                        <Badge color={l.invitation.state === 'Queued' && l.invitation.error ? 'orange' : invitationBadge[l.invitation.state][0]} variant="light">
                          {l.invitation.state === 'Queued' && l.invitation.error ? 'Retrying' : invitationBadge[l.invitation.state][1]}
                        </Badge>
                      </Tooltip>
                    ) : (
                      <Text size="xs" c="dimmed">
                        no e-mail
                      </Text>
                    )}
                    {l.invitation.at && l.invitation.state === 'Sent' && (
                      <Text size="xs" c="dimmed">
                        {when(l.invitation.at)}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{l.expiresAt ? when(l.expiresAt) : '—'}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end" wrap="nowrap">
                      <CopyButton value={fullUrl(l)}>
                        {({ copied, copy }) => (
                          <Button size="xs" variant="subtle" color={copied ? 'teal' : undefined} onClick={copy}>
                            {copied ? 'Copied' : 'Copy link'}
                          </Button>
                        )}
                      </CopyButton>
                      {canInvite(l) && (
                        <Button
                          size="xs"
                          variant="subtle"
                          onClick={() => {
                            invite.reset()
                            setToInvite([l.id])
                          }}
                        >
                          {l.invitation.state === 'NotSent' ? 'Send' : 'Resend'}
                        </Button>
                      )}
                      <Button size="xs" variant="subtle" onClick={() => setCoverageFor(l)}>
                        Free times
                      </Button>
                      {l.status !== 'Booked' && (
                        <Button
                          size="xs"
                          variant="subtle"
                          color={l.status === 'Inactive' ? 'signal' : 'red'}
                          loading={setActive.isPending && setActive.variables?.id === l.id}
                          onClick={() => setActive.mutate(l)}
                        >
                          {l.status === 'Inactive' ? 'Activate' : 'Deactivate'}
                        </Button>
                      )}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <ConfirmModal
        opened={toInvite !== null}
        title="Send invitations"
        message={
          toInvite?.length === 1
            ? `Send the invitation with the booking link to ${who(links.data!.find((l) => l.id === toInvite[0])!)}?`
            : `Send the invitation e-mail to ${toInvite?.length} recruits?`
        }
        confirmLabel="Send"
        loading={invite.isPending}
        error={invite.error}
        onConfirm={() => toInvite && invite.mutate(toInvite)}
        onClose={() => setToInvite(null)}
      />

      <Modal opened={creating} onClose={() => setCreating(false)} title="Create booking links" size="lg" centered>
        {creating && (
          <CreateLinksForm
            sections={(sections.data ?? []).map((s) => ({ value: String(s.id), label: s.name }))}
            onCreated={(l) => {
              queryClient.invalidateQueries({ queryKey: linksKey })
              setCreating(false)
              setCreated(l)
            }}
          />
        )}
      </Modal>

      <Modal opened={created !== null} onClose={() => setCreated(null)} title={`${created?.length} links created`} size="lg" centered>
        {created && (
          <Stack>
            <Text size="sm">Send each recruit their own link. You can also find them in the list later.</Text>
            <Table>
              <Table.Tbody>
                {created.map((l) => (
                  <Table.Tr key={l.id}>
                    <Table.Td>{who(l)}</Table.Td>
                    <Table.Td>
                      <Text size="xs" ff="monospace">
                        {fullUrl(l)}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
            <Group justify="flex-end">
              <CopyButton value={copyText(created)}>
                {({ copied, copy }) => (
                  <Button variant="default" onClick={copy}>
                    {copied ? 'Copied' : 'Copy all'}
                  </Button>
                )}
              </CopyButton>
              <Button variant="default" onClick={() => downloadCsv(created)}>
                Export CSV
              </Button>
              <Button onClick={() => setCreated(null)}>Done</Button>
            </Group>
          </Stack>
        )}
      </Modal>

      <Modal opened={coverageFor !== null} onClose={() => setCoverageFor(null)} title={`Free times: ${coverageFor?.sections.join(' + ')}`} size="xl" centered>
        {coverageFor && <LinkCoverage linkId={coverageFor.id} />}
      </Modal>
    </Stack>
  )
}

function CreateLinksForm({ sections, onCreated }: { sections: { value: string; label: string }[]; onCreated: (links: Link[]) => void }) {
  const [sectionIds, setSectionIds] = useState<string[]>([])
  const [mode, setMode] = useState<'recipients' | 'count'>('recipients')
  const [text, setText] = useState('')
  const [count, setCount] = useState<number | string>(1)
  const [expires, setExpires] = useState('') // date; link works until the end of that day
  const recipients = parseRecipients(text)

  const create = useMutation({
    mutationFn: () =>
      api<Link[]>('/api/booking-links', {
        method: 'POST',
        json: {
          sectionIds: sectionIds.map(Number),
          expiresAt: expires ? new Date(`${expires}T23:59:59`).toISOString() : null,
          ...(mode === 'recipients' ? { recipients } : { count: Number(count) }),
        },
      }),
    onSuccess: onCreated,
  })

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        create.mutate()
      }}
    >
      <Stack>
        {create.isError && <Alert color="red">{errorMessage(create.error)}</Alert>}
        <MultiSelect
          label="Sections"
          description="Recruits applying to several sections need one link with all of them – someone from each section will be at the interview."
          required
          data={sections}
          value={sectionIds}
          onChange={setSectionIds}
          error={fieldError(create.error, 'SectionIds')}
        />
        <SegmentedControl
          value={mode}
          onChange={(v) => setMode(v as typeof mode)}
          data={[
            { value: 'recipients', label: 'For specific people' },
            { value: 'count', label: 'Anonymous links' },
          ]}
        />
        {mode === 'recipients' ? (
          <Textarea
            label="Recruits"
            description="One per line: name, e-mail (or just one of them). You can paste from a spreadsheet."
            placeholder={'Jan Kowalski, jan@example.com\nAnna Nowak, anna@example.com'}
            autosize
            minRows={4}
            maxRows={12}
            required
            value={text}
            onChange={(e) => setText(e.currentTarget.value)}
            error={fieldError(create.error, 'Recipients')}
          />
        ) : (
          <NumberInput label="Number of links" min={1} max={200} required value={count} onChange={setCount} error={fieldError(create.error, 'Count')} />
        )}
        <TextInput type="date" label="Expires (optional)" description="Link stops working after this day." value={expires} onChange={(e) => setExpires(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button type="submit" loading={create.isPending} disabled={!sectionIds.length || (mode === 'recipients' && !recipients.length)}>
            {mode === 'recipients' ? `Create ${recipients.length} links` : `Create ${count || 0} links`}
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
