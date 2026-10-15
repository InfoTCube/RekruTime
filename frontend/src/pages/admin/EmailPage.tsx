import { Alert, Badge, Button, Code, Group, Paper, SegmentedControl, Skeleton, Stack, Table, Text, Title, Tooltip } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../../api'
import { PageHeader } from '../../components/Page'
import { notifySuccess } from '../../notify'
import { TemplateEditor } from './TemplateEditor'

type Settings = { configured: boolean; host: string | null; port: number; fromAddress: string | null; fromName: string; replyTo: string | null }
type Status = 'Pending' | 'Sent' | 'Failed'
type Message = { id: number; to: string; subject: string; status: Status; attempts: number; lastError: string | null; createdAt: string; sentAt: string | null }

const statusColor: Record<Status, string> = { Pending: 'cortex', Sent: 'signal', Failed: 'red' }
const when = (iso: string) => new Date(iso).toLocaleString('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export function EmailPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['email-settings'], queryFn: () => api<Settings>('/api/email/settings') })
  const [status, setStatus] = useState<'all' | Status>('all')
  const messages = useQuery({
    queryKey: ['email-messages', status],
    queryFn: () => api<Message[]>(`/api/email/messages${status === 'all' ? '' : `?status=${status}`}`),
    refetchInterval: 5000, // the sender works in the background – watch statuses change
  })
  const test = useMutation({
    mutationFn: () => api<{ sentTo: string }>('/api/email/test', { method: 'POST' }),
    onSuccess: (r) => notifySuccess(`Sent to ${r.sentTo} – check the inbox (and spam).`, 'Test e-mail sent'),
  })
  const retry = useMutation({
    mutationFn: (id: number) => api(`/api/email/messages/${id}/retry`, { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['email-messages'] }),
  })

  return (
    <Stack>
      <PageHeader title="E-mail" description="Invitations go out through the SMTP server set on the server. Edit the texts below; recent e-mails and their status are at the bottom." />

      <Paper withBorder p="md">
        {settings.isPending && <Skeleton height={60} />}
        {settings.data && (
          <Stack gap="xs">
            {settings.data.configured ? (
              <Text size="sm">
                Sending via <Code>{settings.data.host}:{settings.data.port}</Code> as <b>{settings.data.fromName}</b> &lt;{settings.data.fromAddress}&gt;
                {settings.data.replyTo && <> · replies to {settings.data.replyTo}</>}
              </Text>
            ) : (
              <Alert color="orange" title="E-mail is not configured">
                Set the <Code>Email__…</Code> settings on the server (see <Code>docs/EMAIL.md</Code>). Until then, links can be copied or exported to CSV.
              </Alert>
            )}
            <Group>
              <Button variant="default" loading={test.isPending} disabled={!settings.data.configured} onClick={() => test.mutate()}>
                Send test e-mail to me
              </Button>
            </Group>
            {test.isError && <Alert color="red">{errorMessage(test.error)}</Alert>}
          </Stack>
        )}
      </Paper>

      <TemplateEditor />

      <Group justify="space-between">
        <Title order={4}>Recent e-mails</Title>
        <SegmentedControl value={status} onChange={(v) => setStatus(v as typeof status)} data={['all', 'Pending', 'Sent', 'Failed'].map((v) => ({ value: v, label: v === 'all' ? 'All' : v }))} />
      </Group>
      {messages.isError && <Alert color="red">{errorMessage(messages.error)}</Alert>}
      {retry.isError && <Alert color="red">{errorMessage(retry.error)}</Alert>}
      {messages.data?.length === 0 && <Text c="dimmed">No e-mails.</Text>}
      {!!messages.data?.length && (
        <Table.ScrollContainer minWidth={640}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>To</Table.Th>
                <Table.Th>Subject</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Queued</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {messages.data.map((m) => (
                <Table.Tr key={m.id}>
                  <Table.Td>{m.to}</Table.Td>
                  <Table.Td>{m.subject}</Table.Td>
                  <Table.Td>
                    <Tooltip label={m.lastError} disabled={!m.lastError} multiline w={320}>
                      <Badge color={statusColor[m.status]} variant="light">
                        {m.status}
                        {m.status !== 'Sent' && m.attempts > 0 && ` (${m.attempts}×)`}
                      </Badge>
                    </Tooltip>
                  </Table.Td>
                  <Table.Td>{when(m.sentAt ?? m.createdAt)}</Table.Td>
                  <Table.Td>
                    {m.status !== 'Sent' && (
                      <Button size="xs" variant="subtle" loading={retry.isPending && retry.variables === m.id} onClick={() => retry.mutate(m.id)}>
                        Retry now
                      </Button>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Stack>
  )
}
