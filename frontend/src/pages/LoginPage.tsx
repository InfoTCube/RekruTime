import { Alert, Box, Button, Center, Paper, PasswordInput, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { api, ApiError } from '../api'
import { meKey, useMe, type Me } from '../auth'
import { Logo, LogoMark } from '../components/Logo'
import { SignalTrace } from '../components/SignalTrace'

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
    <SimpleGrid cols={{ base: 1, md: 2 }} spacing={0} mih="100vh">
      {/* Brand panel – desktop only */}
      <Box visibleFrom="md" bg="cortex.9" c="white" p={48} style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
        <LogoMark size={44} />
        <Stack gap="lg">
          <Title order={1} fz={44} lh={1.1} maw={420} c="white">
            Interviews, scheduled without the spreadsheet.
          </Title>
          <Text c="cortex.1" maw={420}>
            Recruiters mark when they're free, recruits pick a time from their personal link, and everyone sees the same calendar.
          </Text>
        </Stack>
        <SignalTrace />
      </Box>
      <Center p="md">
        <Paper withBorder p="xl" w="100%" maw={380} shadow="sm">
          <form
            onSubmit={(e) => {
              e.preventDefault()
              login.mutate()
            }}
          >
            <Stack>
              <Logo size={32} />
              <Text c="dimmed" size="sm" mt={-6}>
                Log in to the recruitment panel.
              </Text>
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
    </SimpleGrid>
  )
}
