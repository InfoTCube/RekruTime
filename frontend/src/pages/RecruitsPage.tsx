import { Alert, Anchor, Badge, Button, Group, Select, Stack, Table, Text } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IconMailForward, IconUserCheck } from '@tabler/icons-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../api'
import { isAdmin, useMe } from '../auth'
import { ConfirmModal } from '../components/ConfirmModal'
import { EmptyState, PageHeader, TableSkeleton } from '../components/Page'
import { notifySuccess } from '../notify'
import { decisionKind, decisionState, recruitsKey, sendDecisions, statusColor, statusLabel, useRecruits, type Application, type ApplicationStatus } from '../recruits'
import { useSections } from '../sections'

const date = (iso: string) => new Date(iso).toLocaleString('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })
const statuses: ApplicationStatus[] = ['Submitted', 'Preselected', 'NotPreselected', 'InterviewInvited', 'Pending', 'Disputed', 'Accepted', 'Rejected']

export function RecruitsPage() {
  const me = useMe()
  const admin = isAdmin(me.data)
  const sections = useSections()
  // Leaders only see their own sections, so only those make sense as a filter.
  const sectionOptions = admin ? (sections.data ?? []) : (me.data?.sections.filter((s) => s.isLeader) ?? [])
  const [sectionId, setSectionId] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const recruits = useRecruits({ sectionId: sectionId ?? undefined, status: status ?? undefined })
  // Unfiltered list: a filter hides applications, so "ready" is only judged on everything.
  const all = useRecruits({})
  const ready = admin ? (all.data ?? []).filter((r) => decisionState(r.applications) === 'ready') : []
  const accepted = ready.filter((r) => decisionKind(r.applications) === 'accepted').length
  const [confirming, setConfirming] = useState(false)
  const queryClient = useQueryClient()
  const send = useMutation({
    mutationFn: () => sendDecisions(ready.map((r) => r.id)),
    onSuccess: (r) => {
      setConfirming(false)
      notifySuccess(`${r.queued} decision e-mail${r.queued === 1 ? '' : 's'} queued.`)
      queryClient.invalidateQueries({ queryKey: recruitsKey })
    },
  })

  return (
    <Stack>
      <PageHeader
        title="Recruits"
        description={
          admin
            ? 'Everyone who applied. A section leader and an admin decide together; a split vote is settled by another admin.'
            : 'Recruits who applied to the sections you lead. You and an admin decide together.'
        }
      >
        {sectionOptions.length > 1 && (
          <Select placeholder="All sections" clearable value={sectionId} onChange={setSectionId} data={sectionOptions.map((s) => ({ value: String(s.id), label: s.name }))} />
        )}
        <Select placeholder="Any status" clearable value={status} onChange={setStatus} data={statuses.map((s) => ({ value: s, label: statusLabel[s] }))} />
        {admin && (
          <Button leftSection={<IconMailForward size={16} />} disabled={!ready.length} onClick={() => (send.reset(), setConfirming(true))}>
            Send decisions ({ready.length})
          </Button>
        )}
      </PageHeader>

      <ConfirmModal
        opened={confirming}
        title="Send decision e-mails"
        message={
          <>
            Send decision e-mails to <b>{ready.length}</b> recruit{ready.length === 1 ? '' : 's'} whose applications are all decided: {accepted} accepted,{' '}
            {ready.length - accepted} rejected. Their votes get locked. Recruits with undecided applications are left out.
          </>
        }
        confirmLabel="Send"
        color="cortex"
        loading={send.isPending}
        error={send.error}
        onConfirm={() => send.mutate()}
        onClose={() => setConfirming(false)}
      />

      {recruits.isPending && <TableSkeleton />}
      {recruits.isError && <Alert color="red">{errorMessage(recruits.error)}</Alert>}
      {recruits.data?.length === 0 && (
        <EmptyState icon={<IconUserCheck size={24} />} title={sectionId || status ? 'No recruits match' : 'No recruits yet'}>
          Recruits appear here once they book an interview.
        </EmptyState>
      )}
      {!!recruits.data?.length && (
        <Table.ScrollContainer minWidth={720}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Applications</Table.Th>
                <Table.Th>Interview</Table.Th>
                <Table.Th>Interviewers</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {recruits.data.map((r) => (
                <Table.Tr key={r.id}>
                  <Table.Td>
                    <Anchor component={Link} to={`/recruits/${r.id}`} fw={500}>
                      {r.fullName}
                    </Anchor>
                    <Text size="xs" c="dimmed">
                      {r.email}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Stack gap={4}>
                      {r.applications.map((a) => (
                        <ApplicationLine key={a.id} application={a} />
                      ))}
                      {decisionState(r.applications) === 'sent' && (
                        <Text size="xs" c="dimmed">
                          ✉ decision sent
                        </Text>
                      )}
                    </Stack>
                  </Table.Td>
                  <Table.Td>{r.interview ? date(r.interview.start) : <Text c="dimmed">—</Text>}</Table.Td>
                  <Table.Td>{r.interview?.interviewers.join(', ') || <Text c="dimmed">—</Text>}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Stack>
  )
}

function ApplicationLine({ application: a }: { application: Application }) {
  const approve = a.votes.filter((v) => v.decision === 'Approve').length
  const reject = a.votes.length - approve
  return (
    <Group gap="xs" wrap="nowrap">
      <Badge variant="light" color={statusColor[a.status]} size="sm">
        {a.sectionName} · {statusLabel[a.status]}
      </Badge>
      {a.votes.length > 0 && (
        <Text size="xs" c="dimmed" title={a.votes.map((v) => `${v.name}: ${v.decision}${v.isFinal ? ' (final)' : ''}`).join('\n')}>
          {approve} for · {reject} against
        </Text>
      )}
    </Group>
  )
}
