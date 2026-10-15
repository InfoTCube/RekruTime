import { Alert, Button, Center, Paper, PasswordInput, Stack, TextInput, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { api, ApiError } from '../api'
import { meKey, useMe, type Me } from '../auth'

export function LoginPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/'
  const me = useMe()

  const login = useMutation({
    mutationFn: () => api<Me>('/api/auth/login', { method: 'POST', json: { email, password } }),
    onSuccess: (user) => {
      queryClient.setQueryData(meKey, user)
      navigate(from, { replace: true })
    },
  })

  if (me.data) return <Navigate to={from} replace />

  const error =
    login.error instanceof ApiError && login.error.status === 401
      ? 'Wrong email or password.'
      : login.error instanceof ApiError && login.error.status === 429
        ? 'Too many failed attempts. Try again in a few minutes.'
        : login.error
          ? 'Something went wrong. Try again.'
          : null

  return (
    <Center mih="100vh" p="md">
      <Paper withBorder p="xl" w="100%" maw={380}>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            login.mutate()
          }}
        >
          <Stack>
            <Title order={2}>RekruTime</Title>
            {error && <Alert color="red">{error}</Alert>}
            <TextInput label="Email" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.currentTarget.value)} />
            <PasswordInput label="Password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.currentTarget.value)} />
            <Button type="submit" loading={login.isPending}>
              Log in
            </Button>
          </Stack>
        </form>
      </Paper>
    </Center>
  )
}
