import { Alert, Button, Group, Loader, Paper, Stack, Text, TextInput, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../../api'
import { formatDay, recruitmentKey, useRecruitmentPeriod, type RecruitmentPeriod } from '../../recruitment'

export function RecruitmentPage() {
  const period = useRecruitmentPeriod()
  return (
    <Stack maw={520}>
      <Title order={2}>Recruitment period</Title>
      <Text c="dimmed" size="sm">
        Interviews can only be booked for days in this period. Recruiters see only these weeks in their availability calendar.
      </Text>
      {period.isPending && <Loader />}
      {period.isError && <Alert color="red">{errorMessage(period.error)}</Alert>}
      {period.data && <PeriodForm key={`${period.data.interviewsFrom}-${period.data.interviewsTo}`} initial={period.data} />}
    </Stack>
  )
}

function PeriodForm({ initial }: { initial: RecruitmentPeriod }) {
  const queryClient = useQueryClient()
  const [from, setFrom] = useState(initial.interviewsFrom ?? '')
  const [to, setTo] = useState(initial.interviewsTo ?? '')
  const save = useMutation({
    mutationFn: (p: RecruitmentPeriod) => api<RecruitmentPeriod>('/api/settings/recruitment', { method: 'PUT', json: p }),
    onSuccess: (p) => queryClient.setQueryData(recruitmentKey, p),
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
            <Alert color="green">
              Booking open for {formatDay(initial.interviewsFrom!)} – {formatDay(initial.interviewsTo!)}.
            </Alert>
          ) : (
            <Alert color="gray">No period set – booking is closed.</Alert>
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
