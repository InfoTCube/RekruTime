import { Alert, Button, Group, Paper, Skeleton, Stack, Text, TextInput } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../../api'
import { PageHeader } from '../../components/Page'
import { notifySuccess } from '../../notify'
import { formatDay, recruitmentKey, useRecruitmentPeriod, type RecruitmentPeriod } from '../../recruitment'

export function RecruitmentPage() {
  const period = useRecruitmentPeriod()
  return (
    <Stack maw={520}>
      <PageHeader title="Recruitment period" description="Interviews can only be booked for days in this period. Recruiters see only these weeks in their availability calendar." />
      {period.isPending && <Skeleton height={160} />}
      {period.isError && <Alert color="red">{errorMessage(period.error)}</Alert>}
      {period.data && <PeriodForm key={`${period.data.interviewsFrom}-${period.data.interviewsTo}`} initial={period.data} />}
      <PurgeRecruitData />
    </Stack>
  )
}

function PeriodForm({ initial }: { initial: RecruitmentPeriod }) {
  const queryClient = useQueryClient()
  const [from, setFrom] = useState(initial.interviewsFrom ?? '')
  const [to, setTo] = useState(initial.interviewsTo ?? '')
  const save = useMutation({
    mutationFn: (p: RecruitmentPeriod) => api<RecruitmentPeriod>('/api/settings/recruitment', { method: 'PUT', json: p }),
    onSuccess: (p) => {
      queryClient.setQueryData(recruitmentKey, p)
      notifySuccess(p.interviewsFrom ? 'Booking period saved.' : 'Booking closed.')
    },
  })
  const isOpen = initial.interviewsFrom && initial.interviewsTo

  return (
    <Paper withBorder p="md">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate({ interviewsFrom: from, interviewsTo: to })
        }}
      >
        <Stack>
          {isOpen ? (
            <Alert color="signal">
              Booking open for {formatDay(initial.interviewsFrom!)} – {formatDay(initial.interviewsTo!)}.
            </Alert>
          ) : (
            <Alert color="cortex">No period set – booking is closed.</Alert>
          )}
          {save.isError && <Alert color="red">{errorMessage(save.error)}</Alert>}
          <Group grow align="flex-start">
            <TextInput type="date" label="First interview day" required value={from} onChange={(e) => setFrom(e.currentTarget.value)} />
            <TextInput type="date" label="Last interview day" required min={from || undefined} value={to} onChange={(e) => setTo(e.currentTarget.value)} />
          </Group>
          <Group justify="flex-end">
            {isOpen && (
              <Button variant="default" loading={save.isPending && !save.variables?.interviewsFrom} onClick={() => save.mutate({ interviewsFrom: null, interviewsTo: null })}>
                Close booking
              </Button>
            )}
            <Button type="submit" loading={save.isPending && !!save.variables?.interviewsFrom}>
              Save
            </Button>
          </Group>
        </Stack>
      </form>
    </Paper>
  )
}

const PURGE_WORDS = 'DELETE ALL'

// GDPR: after the recruitment, wipe every recruit with their interviews and links.
function PurgeRecruitData() {
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState('')
  const purge = useMutation({
    mutationFn: () => api<{ deletedRecruits: number }>('/api/recruits/purge', { method: 'POST', json: { confirm } }),
    onSuccess: (r) => {
      notifySuccess(`Deleted ${r.deletedRecruits} recruits.`)
      setConfirm('')
      queryClient.invalidateQueries({ queryKey: ['interviews'] })
      queryClient.invalidateQueries({ queryKey: ['booking-links'] })
    },
  })
  return (
    <Paper withBorder p="md">
      <Stack>
        <Text fw={600} c="red">
          Delete all recruit data
        </Text>
        <Text size="sm">
          When the recruitment is over, permanently delete all recruits with their interviews, applications and booking links. Users, sections and
          availability stay. This can't be undone.
        </Text>
        {purge.isError && <Alert color="red">{errorMessage(purge.error)}</Alert>}
        <TextInput label={`Type "${PURGE_WORDS}" to confirm`} value={confirm} onChange={(e) => setConfirm(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button color="red" disabled={confirm !== PURGE_WORDS} loading={purge.isPending} onClick={() => purge.mutate()}>
            Delete all recruit data
          </Button>
        </Group>
      </Stack>
    </Paper>
  )
}
