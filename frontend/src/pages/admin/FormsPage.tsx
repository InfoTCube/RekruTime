import { ActionIcon, Alert, Anchor, Button, CopyButton, Group, Stack, Switch, Table, Tooltip } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IconCheck, IconCopy, IconForms, IconPlus, IconTrash } from '@tabler/icons-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../api'
import { ConfirmModal } from '../../components/ConfirmModal'
import { EmptyState, PageHeader, TableSkeleton } from '../../components/Page'
import { formsKey, publicFormUrl, useForms, type Form } from '../../forms'
import { notifySuccess } from '../../notify'

export function FormsPage() {
  const forms = useForms()
  const queryClient = useQueryClient()
  const [deleting, setDeleting] = useState<Form | null>(null)

  const setOpen = useMutation({
    mutationFn: (v: { id: number; isOpen: boolean }) => api(`/api/forms/${v.id}`, { method: 'PATCH', json: { isOpen: v.isOpen } }),
    onSuccess: (_, v) => {
      notifySuccess(v.isOpen ? 'Form opened – recruits can fill it in.' : 'Form closed.')
      queryClient.invalidateQueries({ queryKey: formsKey })
    },
  })

  const remove = useMutation({
    mutationFn: (f: Form) => api(`/api/forms/${f.id}`, { method: 'DELETE' }),
    onSuccess: () => {
      notifySuccess('Form deleted.')
      queryClient.invalidateQueries({ queryKey: formsKey })
      setDeleting(null)
    },
  })

  return (
    <Stack>
      <PageHeader title="Forms" description="Recruitment forms recruits fill in to apply. Name, e-mail and the data-processing consent are on every form; add your own questions and a sections question.">
        <Button component={Link} to="/admin/forms/new" leftSection={<IconPlus size={16} />}>
          New form
        </Button>
      </PageHeader>

      {forms.isPending && <TableSkeleton rows={3} />}
      {forms.isError && <Alert color="red">{errorMessage(forms.error)}</Alert>}
      {setOpen.isError && <Alert color="red">{errorMessage(setOpen.error)}</Alert>}
      {forms.data?.length === 0 && (
        <EmptyState icon={<IconForms size={24} />} title="No forms yet">
          Create a form, add a sections question, then open it and share its link.
        </EmptyState>
      )}
      {!!forms.data?.length && (
        <Table.ScrollContainer minWidth={560}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Title</Table.Th>
                <Table.Th>Open</Table.Th>
                <Table.Th>Responses</Table.Th>
                <Table.Th>Created</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {forms.data.map((f) => (
                <Table.Tr key={f.id}>
                  <Table.Td>
                    <Anchor component={Link} to={`/admin/forms/${f.id}`}>
                      {f.title}
                    </Anchor>
                  </Table.Td>
                  <Table.Td>
                    <Switch
                      aria-label={`${f.title} open`}
                      checked={f.isOpen}
                      disabled={setOpen.isPending && setOpen.variables?.id === f.id}
                      onChange={(e) => (setOpen.reset(), setOpen.mutate({ id: f.id, isOpen: e.currentTarget.checked }))}
                    />
                  </Table.Td>
                  <Table.Td>{f.responses}</Table.Td>
                  <Table.Td>{new Date(f.createdAt).toLocaleDateString()}</Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end" wrap="nowrap">
                      <CopyButton value={publicFormUrl(f.id)}>
                        {({ copied, copy }) => (
                          <Tooltip label={copied ? 'Copied' : 'Copy public link'}>
                            <ActionIcon variant="subtle" color={copied ? 'signal' : 'gray'} aria-label={`Copy link to ${f.title}`} onClick={copy}>
                              {copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
                            </ActionIcon>
                          </Tooltip>
                        )}
                      </CopyButton>
                      <ActionIcon variant="subtle" color="red" aria-label={`Delete ${f.title}`} onClick={() => (remove.reset(), setDeleting(f))}>
                        <IconTrash size={16} />
                      </ActionIcon>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <ConfirmModal
        opened={deleting !== null}
        title="Delete form"
        message={
          <>
            Delete <b>{deleting?.title}</b>? A form that already has responses can't be deleted – close it instead.
          </>
        }
        confirmLabel="Delete"
        loading={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onClose={() => setDeleting(null)}
      />
    </Stack>
  )
}
