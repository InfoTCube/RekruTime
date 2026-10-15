import { Alert, Button, Group, PasswordInput, Stack } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage, fieldError } from '../api'
import { meKey, type Me } from '../auth'
import { notifySuccess } from '../notify'

// Own password: current + new (twice). Server enforces the password rules and tells which field is wrong.
export function ChangePasswordForm({ onDone, submitLabel = 'Change password' }: { onDone?: () => void; submitLabel?: string }) {
  const queryClient = useQueryClient()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const change = useMutation({
    mutationFn: () => api('/api/auth/change-password', { method: 'POST', json: { currentPassword: current, newPassword: next } }),
    onSuccess: () => {
      queryClient.setQueryData<Me | null>(meKey, (me) => me && { ...me, mustChangePassword: false })
      notifySuccess('Password changed.')
      onDone?.()
    },
  })
  const mismatch = repeat !== '' && repeat !== next
  const otherError = change.isError && !fieldError(change.error, 'CurrentPassword') && !fieldError(change.error, 'NewPassword')

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        if (!mismatch) change.mutate()
      }}
    >
      <Stack>
        {otherError && <Alert color="red">{errorMessage(change.error)}</Alert>}
        <PasswordInput
          label="Current password"
          autoComplete="current-password"
          required
          value={current}
          onChange={(e) => setCurrent(e.currentTarget.value)}
          error={fieldError(change.error, 'CurrentPassword')}
        />
        <PasswordInput
          label="New password"
          description="At least 6 characters with an upper- and lowercase letter, a digit and a symbol."
          autoComplete="new-password"
          required
          value={next}
          onChange={(e) => setNext(e.currentTarget.value)}
          error={fieldError(change.error, 'NewPassword')}
        />
        <PasswordInput
          label="Repeat new password"
          autoComplete="new-password"
          required
          value={repeat}
          onChange={(e) => setRepeat(e.currentTarget.value)}
          error={mismatch && "Passwords don't match."}
        />
        <Group justify="flex-end">
          <Button type="submit" loading={change.isPending}>
            {submitLabel}
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
