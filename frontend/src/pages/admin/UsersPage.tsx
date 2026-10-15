import { ActionIcon, Alert, Badge, Button, Code, CopyButton, Group, Modal, MultiSelect, SegmentedControl, Stack, Table, Text, TextInput } from '@mantine/core'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../../api'
import { useMe } from '../../auth'
import { ConfirmModal } from '../../components/ConfirmModal'
import { PageHeader, TableSkeleton } from '../../components/Page'
import { notifySuccess } from '../../notify'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { useSections } from '../../sections'

type User = { id: string; email: string; displayName: string; role: 'Admin' | 'Recruiter'; sectionIds: number[] }
type UserForm = Omit<User, 'id'>
const empty: UserForm = { email: '', displayName: '', role: 'Recruiter', sectionIds: [] }
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

  const save = useMutation({
    mutationFn: (form: UserForm): Promise<User | { user: User; temporaryPassword: string }> =>
      editing === 'new'
        ? api<{ user: User; temporaryPassword: string }>('/api/users', { method: 'POST', json: form })
        : api<User>(`/api/users/${(editing as User).id}`, { method: 'PUT', json: form }),
    onSuccess: (result) => {
      refresh()
      setEditing(null)
      if (!('temporaryPassword' in result)) notifySuccess('User saved.')
      if ('temporaryPassword' in result) setPassword({ user: result.user.email, password: result.temporaryPassword })
    },
  })
  const remove = useMutation({
    mutationFn: (u: User) => api(`/api/users/${u.id}`, { method: 'DELETE' }),
    onSuccess: () => (notifySuccess('User deleted.'), refresh(), setDeleting(null)),
  })
  const reset = useMutation({
    mutationFn: (u: User) => api<{ temporaryPassword: string }>(`/api/users/${u.id}/reset-password`, { method: 'POST' }),
    onSuccess: (r, u) => (setResetting(null), setPassword({ user: u.email, password: r.temporaryPassword })),
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
                          <Badge key={id} variant="outline" size="sm">
                            {sectionName(id)}
                          </Badge>
                        ))}
                      </Group>
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
            Generate a new temporary password for <b>{resetting?.displayName}</b>? The old one stops working and they are logged out.
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
            Send this to <b>{password?.user}</b>. It is shown only once.
          </Text>
          <Group justify="space-between" wrap="nowrap">
            <Code fz="lg">{password?.password}</Code>
            <CopyButton value={password?.password ?? ''}>
              {({ copied, copy }) => (
                <Button size="xs" color={copied ? 'teal' : undefined} onClick={copy}>
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
          onChange={(v) => setForm({ ...form, sectionIds: v.map(Number) })}
          searchable
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
