import { Alert, Anchor, Button, Center, Paper, PasswordInput, Stack, Text, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api, errorMessage, fieldError } from '../api'
import { meKey, type Me } from '../auth'
import { Logo } from '../components/Logo'

// Public: the link from the account invite / new-password e-mail. Sets the password and logs in.
export function SetPasswordPage() {
  const [params] = useSearchParams()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const set = useMutation({
    mutationFn: () => api<Me>('/api/auth/set-password', { method: 'POST', json: { userId: params.get('u'), token: params.get('t'), newPassword: password } }),
    onSuccess: (me) => {
      queryClient.setQueryData(meKey, me)
      navigate('/', { replace: true })
    },
  })
  const mismatch = repeat !== '' && repeat !== password
  const linkError = fieldError(set.error, 'Token') ?? (!params.get('u') || !params.get('t') ? 'This link is incomplete. Open it straight from the e-mail.' : undefined)
  const otherError = set.isError && !fieldError(set.error, 'Token') && !fieldError(set.error, 'NewPassword')

  return (
    <Center mih="100vh" p="md">
      <Paper withBorder shadow="sm" p="xl" w="100%" maw={420}>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            if (!mismatch) set.mutate()
          }}
        >
          <Stack>
            <Logo size={28} />
            <Title order={3}>Set your password</Title>
            <Text size="sm" c="dimmed">
              Choose the password for the recruitment panel. You log in with your e-mail address.
            </Text>
            {linkError && (
              <Alert color="red">
                {linkError}{' '}
                <Anchor component={Link} to="/login" size="sm">
                  Go to login
                </Anchor>
              </Alert>
            )}
            {otherError && <Alert color="red">{errorMessage(set.error)}</Alert>}
            <PasswordInput
              label="New password"
              description="At least 6 characters with an upper- and lowercase letter, a digit and a symbol."
              autoComplete="new-password"
              required
              data-autofocus
              value={password}
              onChange={(e) => setPassword(e.currentTarget.value)}
              error={fieldError(set.error, 'NewPassword')}
            />
            <PasswordInput
              label="Repeat new password"
              autoComplete="new-password"
              required
              value={repeat}
              onChange={(e) => setRepeat(e.currentTarget.value)}
              error={mismatch && "Passwords don't match."}
            />
            <Button type="submit" loading={set.isPending} disabled={!params.get('u') || !params.get('t')}>
              Save and log in
            </Button>
          </Stack>
        </form>
      </Paper>
    </Center>
  )
}
