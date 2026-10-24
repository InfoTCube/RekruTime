import { ActionIcon, Alert, Anchor, Badge, Button, Code, CopyButton, Group, Modal, MultiSelect, SegmentedControl, Stack, Table, Text, TextInput } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage, fieldError } from '../../api'
import { useMe } from '../../auth'
import { ConfirmModal } from '../../components/ConfirmModal'
import { PageHeader, TableSkeleton } from '../../components/Page'
import { notifySuccess } from '../../notify'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { useSections } from '../../sections'

type User = { id: string; email: string; displayName: string; role: 'Admin' | 'Recruiter'; sectionIds: number[]; leaderSectionIds: number[]; availableHours: number }
type UserForm = Omit<User, 'id' | 'availableHours'>
type Created = { user: User; temporaryPassword: string | null; linkSent: boolean }
const empty: UserForm = { email: '', displayName: '', role: 'Recruiter', sectionIds: [], leaderSectionIds: [] }
const usersKey = ['users']

export function UsersPage() {
  const me = useMe()
  const users = useQuery({ queryKey: usersKey, queryFn: () => api<User[]>('/api/users') })
  const sections = useSections()
  const sectionName = (id: number) => sections.data?.find((s) => s.id === id)?.name ?? '?'
  const queryClient = useQueryClient()
  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: usersKey })
    queryClient.invalidateQueries({ queryKey: ['me'] }) // editing yourself changes your name/sections
  }

  const [editing, setEditing] = useState<User | 'new' | null>(null)
  const [deleting, setDeleting] = useState<User | null>(null)
  const [resetting, setResetting] = useState<User | null>(null)
  const [password, setPassword] = useState<{ user: string; password: string } | null>(null)
  // E-mail set up → the server e-mails a link instead of returning a temporary password.
  const linkSent = (email: string, invite: boolean) =>
    notifySuccess(`${invite ? 'Invitation' : 'Link to set a new password'} e-mailed to ${email}. It works for 3 days.`)

  const save = useMutation({
    mutationFn: (form: UserForm): Promise<User | Created> =>
      editing === 'new'
        ? api<Created>('/api/users', { method: 'POST', json: form })
        : api<User>(`/api/users/${(editing as User).id}`, { method: 'PUT', json: form }),
    onSuccess: (result) => {
      refresh()
      setEditing(null)
      if (!('user' in result)) notifySuccess('User saved.')
      else if (result.temporaryPassword) setPassword({ user: result.user.email, password: result.temporaryPassword })
      else linkSent(result.user.email, true)
    },
  })
  const remove = useMutation({
    mutationFn: (u: User) => api(`/api/users/${u.id}`, { method: 'DELETE' }),
    onSuccess: () => (notifySuccess('User deleted.'), refresh(), setDeleting(null)),
  })
  const reset = useMutation({
    mutationFn: (u: User) => api<{ temporaryPassword: string | null }>(`/api/users/${u.id}/reset-password`, { method: 'POST' }),
    onSuccess: (r, u) => {
      setResetting(null)
      if (r.temporaryPassword) setPassword({ user: u.email, password: r.temporaryPassword })
      else linkSent(u.email, false)
    },
  })

  return (
    <Stack>
      <PageHeader title="Users" description="Admins manage the recruitment; recruiters add their availability and see their own interviews.">
        <Button leftSection={<IconPlus size={16} />} onClick={() => (save.reset(), setEditing('new'))}>
          Add user
        </Button>
      </PageHeader>

      {users.isPending && <TableSkeleton />}
      {users.isError && <Alert color="red">{errorMessage(users.error)}</Alert>}
      {users.data && (
        <Table.ScrollContainer minWidth={640}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Email</Table.Th>
                <Table.Th>Role</Table.Th>
                <Table.Th>Sections</Table.Th>
                <Table.Th>Availability</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {users.data.map((u) => {
                const isMe = u.id === me.data?.id
                return (
                  <Table.Tr key={u.id}>
                    <Table.Td>
                      {u.displayName} {isMe && <Text span c="dimmed" size="sm">(you)</Text>}
                    </Table.Td>
                    <Table.Td>{u.email}</Table.Td>
                    <Table.Td>
                      <Badge variant="light" color={u.role === 'Admin' ? 'cortex' : 'signal'}>
                        {u.role === 'Admin' ? 'Admin (board)' : 'Recruiter'}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        {u.sectionIds.map((id) => (
                          <Badge key={id} variant={u.leaderSectionIds.includes(id) ? 'filled' : 'outline'} size="sm" title={u.leaderSectionIds.includes(id) ? 'Section leader' : undefined}>
                            {sectionName(id)}
                            {u.leaderSectionIds.includes(id) && ' · leader'}
                          </Badge>
                        ))}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      {/* Upcoming hours – opens their calendar */}
                      <Anchor component={Link} to={`/availability?user=${u.id}`} underline="never">
                        <Badge variant="light" color={u.availableHours > 0 ? 'signal' : 'red'}>
                          {u.availableHours > 0 ? `${u.availableHours} h` : 'Not filled'}
                        </Badge>
                      </Anchor>
                    </Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="flex-end" wrap="nowrap">
                        <Button size="xs" variant="subtle" onClick={() => (save.reset(), setEditing(u))}>
                          Edit
                        </Button>
                        <Button size="xs" variant="subtle" color="orange" onClick={() => (reset.reset(), setResetting(u))}>
                          Reset password
                        </Button>
                        <ActionIcon variant="subtle" color="red" disabled={isMe} aria-label={`Delete ${u.displayName}`} onClick={() => (remove.reset(), setDeleting(u))}>
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'Add user' : 'Edit user'} centered>
        {editing !== null && (
          <UserFormView
            key={editing === 'new' ? 'new' : editing.id}
            initial={editing === 'new' ? empty : editing}
            isMe={editing !== 'new' && editing.id === me.data?.id}
            sections={(sections.data ?? []).map((s) => ({ value: String(s.id), label: s.name }))}
            error={save.error}
            saving={save.isPending}
            onSubmit={(form) => save.mutate(form)}
          />
        )}
      </Modal>

      <ConfirmModal
        opened={deleting !== null}
        title="Delete user"
        message={
          <>
            Delete <b>{deleting?.displayName}</b>? Their availability is deleted too.
          </>
        }
        confirmLabel="Delete"
        loading={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />

      <ConfirmModal
        opened={resetting !== null}
        title="Reset password"
        message={
          <>
            Let <b>{resetting?.displayName}</b> set a new password? With e-mail set up they get a link (their current password works until they use it);
            otherwise you get a temporary password to pass on, and the old one stops working.
          </>
        }
        confirmLabel="Reset"
        color="orange"
        loading={reset.isPending}
        error={reset.error}
        onConfirm={() => resetting && reset.mutate(resetting)}
        onClose={() => setResetting(null)}
      />

      <Modal opened={password !== null} onClose={() => setPassword(null)} title="Temporary password" centered>
        <Stack>
          <Text size="sm">
            Send this to <b>{password?.user}</b>. It is shown only once – after logging in they have to set their own password.
          </Text>
          <Group justify="space-between" wrap="nowrap">
            <Code fz="lg">{password?.password}</Code>
            <CopyButton value={password?.password ?? ''}>
              {({ copied, copy }) => (
                <Button size="xs" color={copied ? 'signal' : undefined} onClick={copy}>
                  {copied ? 'Copied' : 'Copy'}
                </Button>
              )}
            </CopyButton>
          </Group>
          <Group justify="flex-end">
            <Button onClick={() => setPassword(null)}>Done</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}

function UserFormView(props: {
  initial: UserForm
  isMe: boolean
  sections: { value: string; label: string }[]
  error: unknown
  saving: boolean
  onSubmit: (f: UserForm) => void
}) {
  const [form, setForm] = useState<UserForm>({
    email: props.initial.email,
    displayName: props.initial.displayName,
    role: props.initial.role,
    sectionIds: props.initial.sectionIds,
    leaderSectionIds: props.initial.leaderSectionIds,
  })
  const fieldErrorsShown = ['Email', 'DisplayName', 'Role', 'SectionIds'].some((f) => fieldError(props.error, f))

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        props.onSubmit(form)
      }}
    >
      <Stack>
        {!!props.error && !fieldErrorsShown && <Alert color="red">{errorMessage(props.error)}</Alert>}
        <TextInput label="Name" required data-autofocus value={form.displayName} error={fieldError(props.error, 'DisplayName')} onChange={(e) => setForm({ ...form, displayName: e.currentTarget.value })} />
        <TextInput label="Email" type="email" required value={form.email} error={fieldError(props.error, 'Email')} onChange={(e) => setForm({ ...form, email: e.currentTarget.value })} />
        <Stack gap={4}>
          <Text size="sm" fw={500}>
            Role
          </Text>
          <SegmentedControl
            disabled={props.isMe} // can't demote yourself
            value={form.role}
            onChange={(v) => setForm({ ...form, role: v as UserForm['role'] })}
            data={[
              { value: 'Recruiter', label: 'Recruiter' },
              { value: 'Admin', label: 'Admin (board)' },
            ]}
          />
        </Stack>
        <MultiSelect
          label="Sections"
          placeholder={form.sectionIds.length ? undefined : 'None'}
          data={props.sections}
          value={form.sectionIds.map(String)}
          error={fieldError(props.error, 'SectionIds')}
          onChange={(v) => {
            const sectionIds = v.map(Number)
            setForm({ ...form, sectionIds, leaderSectionIds: form.leaderSectionIds.filter((id) => sectionIds.includes(id)) })
          }}
          searchable
          clearable
        />
        <MultiSelect
          label="Section leader of"
          description="Section leaders read interview notes and vote on recruits of their sections."
          placeholder={form.leaderSectionIds.length ? undefined : 'None'}
          data={props.sections.filter((s) => form.sectionIds.includes(Number(s.value)))}
          value={form.leaderSectionIds.map(String)}
          onChange={(v) => setForm({ ...form, leaderSectionIds: v.map(Number) })}
          disabled={!form.sectionIds.length}
          clearable
        />
        <Group justify="flex-end">
          <Button type="submit" loading={props.saving}>
            Save
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
