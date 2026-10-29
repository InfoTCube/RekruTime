import { Alert, Anchor, Badge, Button, Group, Paper, Stack, Text, Textarea, Title } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, errorMessage } from '../api'
import { isAdmin, useMe } from '../auth'
import { ConfirmModal } from '../components/ConfirmModal'
import { PageHeader, TableSkeleton } from '../components/Page'
import { statusColor as interviewColor } from '../interviews'
import { notifySuccess } from '../notify'
import { decisionKind, decisionState, recruitsKey, sendDecisions, statusColor, statusLabel, type Application, type Decision, type RecruitDetail } from '../recruits'
import { OtherNote } from './InterviewPage'

const when = (iso: string) => new Date(iso).toLocaleString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export function RecruitPage() {
  const id = Number(useParams().id)
  const me = useMe()
  const detail = useQuery({ queryKey: [...recruitsKey, id], queryFn: () => api<RecruitDetail>(`/api/recruits/${id}`) })
  const r = detail.data?.recruit

  return (
    <Stack>
      <Anchor component={Link} to="/recruits" size="sm">
        ← Recruits
      </Anchor>
      <PageHeader
        title={r?.fullName ?? 'Recruit'}
        description={
          r && (
            <>
              <Anchor href={`mailto:${r.email}`} size="sm">
                {r.email}
              </Anchor>
              {r.phone && ` · ${r.phone}`}
              {r.discordNickname && ` · Discord: ${r.discordNickname}`}
            </>
          )
        }
      />
      {detail.isPending && <TableSkeleton rows={3} />}
      {detail.isError && <Alert color="red">{errorMessage(detail.error)}</Alert>}

      {detail.data && (
        <>
          <Title order={3}>Applications</Title>
          {detail.data.applications.length === 0 && (
            <Text c="dimmed" size="sm">
              No applications.
            </Text>
          )}
          {detail.data.applications.map((a) => (
            <ApplicationCard key={a.id} application={a} />
          ))}
          {isAdmin(me.data) && detail.data.applications.length > 0 && <DecisionPanel recruitId={id} name={detail.data.recruit.fullName} applications={detail.data.applications} />}

          <Title order={3} mt="md">
            Interviews and notes
          </Title>
          {detail.data.interviews.length === 0 && (
            <Text c="dimmed" size="sm">
              No interviews you can see.
            </Text>
          )}
          {detail.data.interviews.map(({ interview: i, notes }) => (
            <Paper key={i.id} withBorder p="md" opacity={i.status === 'Cancelled' ? 0.6 : 1}>
              <Stack gap="xs">
                <Group justify="space-between">
                  <Group gap="xs">
                    <Text fw={600}>{when(i.start)}</Text>
                    {i.status !== 'Scheduled' && (
                      <Badge color={interviewColor[i.status]} variant="light">
                        {i.status}
                      </Badge>
                    )}
                    {i.sections.map((s) => (
                      <Badge key={s.id} variant="outline" size="sm">
                        {s.name}
                      </Badge>
                    ))}
                  </Group>
                  <Button size="xs" variant="light" component={Link} to={`/interviews/${i.id}`}>
                    {notes.canWrite ? 'Write your note' : 'Open'}
                  </Button>
                </Group>
                <Text size="sm" c="dimmed">
                  with {i.interviewers.map((p) => p.name).join(' + ')}
                </Text>
                {notes.notes.length === 0 && (
                  <Text size="sm" c="dimmed">
                    No notes yet.
                  </Text>
                )}
                {notes.notes.map((n) => (
                  <OtherNote key={`${n.authorId}-${n.authorName}`} note={n} />
                ))}
              </Stack>
            </Paper>
          ))}
        </>
      )}
    </Stack>
  )
}

// Admins send the decision e-mail by hand once everything is decided; it locks the votes.
function DecisionPanel({ recruitId, name, applications }: { recruitId: number; name: string; applications: Application[] }) {
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const send = useMutation({
    mutationFn: () => sendDecisions([recruitId]),
    onSuccess: (r) => {
      setConfirming(false)
      if (r.queued) notifySuccess('Decision e-mail queued.')
      queryClient.invalidateQueries({ queryKey: recruitsKey })
    },
  })
  const state = decisionState(applications)
  const kind = decisionKind(applications)
  const accepted = applications.filter((a) => a.status === 'Accepted').map((a) => a.sectionName)
  const sentAt = applications.find((a) => a.decisionSentAt)?.decisionSentAt

  if (state === 'sent')
    return (
      <Alert color="gray" title="Decision e-mail sent">
        Queued on {when(sentAt!)} – see Admin → E-mail for delivery. Votes are locked.
      </Alert>
    )
  if (state === 'waiting')
    return (
      <Text size="sm" c="dimmed">
        The decision e-mail can be sent once every application is accepted or rejected.
      </Text>
    )
  return (
    <Alert color={kind === 'accepted' ? 'signal' : 'red'} title="Ready to send the decision">
      <Stack gap="xs">
        <Text size="sm">
          {kind === 'accepted' ? `${name} gets the "accepted" e-mail (${accepted.join(', ')}).` : `${name} gets the "rejected" e-mail.`} Sending locks the votes.
        </Text>
        <Group>
          <Button size="xs" onClick={() => (send.reset(), setConfirming(true))}>
            Send decision e-mail
          </Button>
        </Group>
      </Stack>
      <ConfirmModal
        opened={confirming}
        title="Send decision e-mail"
        message={
          <>
            Send the <b>{kind}</b> e-mail to <b>{name}</b>? After this the votes can't be changed.
          </>
        }
        confirmLabel="Send"
        color={kind === 'accepted' ? 'signal' : 'red'}
        loading={send.isPending}
        error={send.error}
        onConfirm={() => send.mutate()}
        onClose={() => setConfirming(false)}
      />
    </Alert>
  )
}

function ApplicationCard({ application: a }: { application: Application }) {
  const me = useMe()
  const queryClient = useQueryClient()
  const myVote = a.votes.find((v) => v.userId === me.data?.id)
  const [comment, setComment] = useState(myVote?.comment ?? '')
  const [finalDecision, setFinalDecision] = useState<Decision | null>(null)
  const vote = useMutation({
    mutationFn: (body: { decision: Decision; isFinal?: boolean }) => api(`/api/applications/${a.id}/votes`, { method: 'POST', json: { ...body, comment } }),
    onSuccess: () => {
      setFinalDecision(null)
      queryClient.invalidateQueries({ queryKey: recruitsKey }) // list statuses change too
    },
  })
  const decided = a.votes.some((v) => v.isFinal)

  return (
    <Paper withBorder p="md">
      <Stack gap="sm">
        <Group gap="xs">
          <Text fw={600}>{a.sectionName}</Text>
          <Badge color={statusColor[a.status]} variant="light">
            {statusLabel[a.status]}
          </Badge>
        </Group>

        {a.votes.length === 0 && (
          <Text size="sm" c="dimmed">
            No votes yet.
          </Text>
        )}
        {a.votes.map((v) => (
          <Group key={v.userId} gap="xs" align="flex-start" wrap="nowrap">
            <Badge variant={v.isFinal ? 'filled' : 'light'} color={v.decision === 'Approve' ? 'signal' : 'red'} miw={90}>
              {v.isFinal ? `Final: ${v.decision}` : v.decision}
            </Badge>
            <Text size="sm">
              <b>{v.name}</b>
              {v.comment && <> – {v.comment}</>}
            </Text>
          </Group>
        ))}

        {vote.isError && !finalDecision && <Alert color="red">{errorMessage(vote.error)}</Alert>}
        {a.canVote && (
          <>
            <Textarea placeholder="Comment (optional)" value={comment} onChange={(e) => setComment(e.currentTarget.value)} autosize minRows={1} maxLength={2000} />
            <Group gap="xs">
              {(['Approve', 'Reject'] as const).map((d) => (
                <Button
                  key={d}
                  size="xs"
                  color={d === 'Approve' ? 'signal' : 'red'}
                  variant={myVote?.decision === d ? 'filled' : 'light'}
                  loading={vote.isPending && !vote.variables?.isFinal && vote.variables?.decision === d}
                  onClick={() => vote.mutate({ decision: d })}
                >
                  {d}
                  {myVote?.decision === d && ' ✓'}
                </Button>
              ))}
              {myVote && (
                <Text size="xs" c="dimmed">
                  You can change your vote until a final decision.
                </Text>
              )}
            </Group>
          </>
        )}

        {a.canDecide && (
          <Alert color="spark" title="Disputed – your decision">
            <Stack gap="xs">
              <Text size="sm">The section leader and an admin disagree. You haven't voted, so you can make the final decision. It can't be changed afterwards.</Text>
              <Group gap="xs">
                <Button size="xs" color="signal" onClick={() => (vote.reset(), setFinalDecision('Approve'))}>
                  Accept (final)
                </Button>
                <Button size="xs" color="red" onClick={() => (vote.reset(), setFinalDecision('Reject'))}>
                  Reject (final)
                </Button>
              </Group>
            </Stack>
          </Alert>
        )}
        {a.decisionSentAt ? (
          <Text size="xs" c="dimmed">
            Decision e-mail sent – votes are locked.
          </Text>
        ) : (
          decided && (
            <Text size="xs" c="dimmed">
              Final decision made – votes are locked.
            </Text>
          )
        )}
      </Stack>

      <ConfirmModal
        opened={finalDecision !== null}
        title="Final decision"
        message={
          <>
            {finalDecision === 'Approve' ? 'Accept' : 'Reject'} this application to <b>{a.sectionName}</b>? This settles the dispute and locks all votes.
          </>
        }
        confirmLabel={finalDecision === 'Approve' ? 'Accept' : 'Reject'}
        color={finalDecision === 'Approve' ? 'signal' : 'red'}
        loading={vote.isPending}
        error={vote.error}
        onConfirm={() => finalDecision && vote.mutate({ decision: finalDecision, isFinal: true })}
        onClose={() => setFinalDecision(null)}
      />
    </Paper>
  )
}
