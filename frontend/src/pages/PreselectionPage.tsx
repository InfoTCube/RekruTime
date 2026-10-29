import { ActionIcon, Alert, Anchor, Badge, Button, Checkbox, Group, Paper, SegmentedControl, Select, Stack, Text, Tooltip } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { IconCheck, IconFilter, IconX } from '@tabler/icons-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../api'
import { isAdmin, useMe } from '../auth'
import { EmptyState, PageHeader, TableSkeleton } from '../components/Page'
import type { FormField } from '../forms'
import { notifySuccess } from '../notify'
import { statusColor, statusLabel, type ApplicationStatus } from '../recruits'
import { useSections } from '../sections'

type Applicant = {
  applicationId: number
  recruitId: number
  fullName: string
  email: string
  status: ApplicationStatus
  formId: number | null
  submittedAt: string | null
  answers: { fieldId: string; values: string[] }[]
}
type Preselection = { forms: { id: number; title: string; fields: FormField[] }[]; applicants: Applicant[] }
type Settable = 'Submitted' | 'Preselected' | 'NotPreselected'

const filters: { value: ApplicationStatus | 'all'; label: string }[] = [
  { value: 'Submitted', label: 'To review' },
  { value: 'Preselected', label: 'Preselected' },
  { value: 'NotPreselected', label: 'Not preselected' },
  { value: 'InterviewInvited', label: 'Invited' },
  { value: 'all', label: 'All' },
]
const date = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

// Before the interviews: read what applicants answered in the form, mark who gets invited.
export function PreselectionPage() {
  const me = useMe()
  const sections = useSections()
  const options = isAdmin(me.data) ? (sections.data ?? []) : (me.data?.sections.filter((s) => s.isLeader) ?? [])
  const [picked, setPicked] = useState<string | null>(null)
  const sectionId = picked ?? (options[0] ? String(options[0].id) : null)
  const [filter, setFilter] = useState<ApplicationStatus | 'all'>('Submitted')
  const [selected, setSelected] = useState<number[]>([])
  const queryClient = useQueryClient()

  const list = useQuery({
    queryKey: ['preselection', sectionId],
    queryFn: () => api<Preselection>(`/api/preselection?sectionId=${sectionId}`),
    enabled: sectionId !== null,
  })
  const applicants = list.data?.applicants ?? []
  const shown = applicants.filter((a) => filter === 'all' || a.status === filter)
  const selectable = shown.filter((a) => a.status !== 'InterviewInvited').map((a) => a.applicationId)
  const chosen = selected.filter((id) => selectable.includes(id))

  const mark = useMutation({
    mutationFn: (v: { ids: number[]; status: Settable }) =>
      api<{ updated: number; skipped: number }>('/api/preselection', { method: 'POST', json: { applicationIds: v.ids, status: v.status } }),
    onSuccess: (r, v) => {
      notifySuccess(`${r.updated} marked as ${statusLabel[v.status].toLowerCase()}${r.skipped ? ` (${r.skipped} already invited – skipped)` : ''}.`)
      setSelected([])
      queryClient.invalidateQueries({ queryKey: ['preselection'] })
      queryClient.invalidateQueries({ queryKey: ['recruits'] })
    },
  })

  const sectionName = (id: string) => sections.data?.find((s) => String(s.id) === id)?.name ?? id
  const answerText = (field: FormField, values: string[]) => (field.type === 'Sections' ? values.map(sectionName) : values).join(', ')

  return (
    <Stack>
      <PageHeader title="Preselection" description="Applicants from the recruitment form. Read their answers and mark who you want to interview – invitations go to the preselected ones.">
        {options.length > 1 && (
          <Select aria-label="Section" allowDeselect={false} value={sectionId} onChange={(v) => (setPicked(v), setSelected([]))} data={options.map((s) => ({ value: String(s.id), label: s.name }))} />
        )}
      </PageHeader>

      <Group justify="space-between">
        <SegmentedControl
          value={filter}
          onChange={(v) => (setFilter(v as typeof filter), setSelected([]))}
          data={filters.map((f) => ({ value: f.value, label: `${f.label} (${f.value === 'all' ? applicants.length : applicants.filter((a) => a.status === f.value).length})` }))}
        />
        <Group gap="xs">
          <Checkbox
            label="Select all"
            disabled={!selectable.length}
            checked={!!selectable.length && chosen.length === selectable.length}
            indeterminate={chosen.length > 0 && chosen.length < selectable.length}
            onChange={(e) => setSelected(e.currentTarget.checked ? selectable : [])}
          />
          <Button size="xs" color="signal" disabled={!chosen.length} loading={mark.isPending && mark.variables?.status === 'Preselected'} onClick={() => mark.mutate({ ids: chosen, status: 'Preselected' })}>
            Preselect ({chosen.length})
          </Button>
          <Button size="xs" color="red" variant="light" disabled={!chosen.length} loading={mark.isPending && mark.variables?.status === 'NotPreselected'} onClick={() => mark.mutate({ ids: chosen, status: 'NotPreselected' })}>
            Not preselected ({chosen.length})
          </Button>
          {filter !== 'Submitted' && (
            <Button size="xs" variant="default" disabled={!chosen.length} loading={mark.isPending && mark.variables?.status === 'Submitted'} onClick={() => mark.mutate({ ids: chosen, status: 'Submitted' })}>
              Back to review ({chosen.length})
            </Button>
          )}
        </Group>
      </Group>

      {mark.isError && <Alert color="red">{errorMessage(mark.error)}</Alert>}
      {list.isError && <Alert color="red">{errorMessage(list.error)}</Alert>}
      {(list.isPending && sectionId !== null) && <TableSkeleton rows={3} />}
      {sectionId === null && !sections.isPending && (
        <EmptyState icon={<IconFilter size={24} />} title="No sections">
          Add sections first – applicants choose them in the form.
        </EmptyState>
      )}
      {list.data && shown.length === 0 && (
        <EmptyState icon={<IconFilter size={24} />} title="Nobody here">
          {applicants.length ? 'No applicants with this status.' : 'Nobody has applied to this section through a form yet.'}
        </EmptyState>
      )}

      {shown.map((a) => {
        const form = list.data?.forms.find((f) => f.id === a.formId)
        const settable = a.status !== 'InterviewInvited'
        return (
          <Paper key={a.applicationId} withBorder p="md">
            <Stack gap="sm">
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <Group gap="sm" wrap="nowrap" align="flex-start">
                  <Checkbox
                    mt={3}
                    aria-label={`Select ${a.fullName}`}
                    disabled={!settable}
                    checked={selected.includes(a.applicationId)}
                    onChange={(e) => setSelected(e.currentTarget.checked ? [...selected, a.applicationId] : selected.filter((id) => id !== a.applicationId))}
                  />
                  <div>
                    <Group gap="xs">
                      <Anchor component={Link} to={`/recruits/${a.recruitId}`} fw={600}>
                        {a.fullName}
                      </Anchor>
                      <Badge size="sm" variant="light" color={statusColor[a.status]}>
                        {statusLabel[a.status]}
                      </Badge>
                    </Group>
                    <Text size="sm" c="dimmed">
                      {a.email}
                      {a.submittedAt && ` · applied ${date(a.submittedAt)}`}
                    </Text>
                  </div>
                </Group>
                {settable && (
                  <Group gap={4} wrap="nowrap">
                    <Tooltip label="Preselect">
                      <ActionIcon variant={a.status === 'Preselected' ? 'filled' : 'light'} color="signal" aria-label={`Preselect ${a.fullName}`} onClick={() => mark.mutate({ ids: [a.applicationId], status: 'Preselected' })}>
                        <IconCheck size={16} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label="Not preselected">
                      <ActionIcon variant={a.status === 'NotPreselected' ? 'filled' : 'light'} color="red" aria-label={`Don't preselect ${a.fullName}`} onClick={() => mark.mutate({ ids: [a.applicationId], status: 'NotPreselected' })}>
                        <IconX size={16} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                )}
              </Group>
              {form?.fields.map((f) => {
                const values = a.answers.find((x) => x.fieldId === f.id)?.values
                if (!values?.length) return null
                return (
                  <div key={f.id}>
                    <Text size="xs" c="dimmed" fw={600}>
                      {f.label}
                    </Text>
                    <Text size="sm" style={{ whiteSpace: 'pre-line' }}>
                      {answerText(f, values)}
                    </Text>
                  </div>
                )
              })}
            </Stack>
          </Paper>
        )
      })}
    </Stack>
  )
}
