import { AppShell, Burger, Button, Center, Group, Loader, NavLink, Text, Title } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Navigate, Outlet, NavLink as RouterLink, useLocation } from 'react-router'
import { api } from './api'
import { isAdmin, meKey, useMe } from './auth'

const nav = [
  { to: '/availability', label: 'My availability' },
  { to: '/interviews', label: 'Interviews' },
  { to: '/admin/recruitment', label: 'Recruitment period', admin: true },
  { to: '/admin/links', label: 'Booking links', admin: true },
  { to: '/admin/users', label: 'Users', admin: true },
  { to: '/admin/sections', label: 'Sections', admin: true },
]

// Guard + shell for every logged-in page.
export function Layout() {
  const me = useMe()
  const location = useLocation()
  const [opened, { toggle, close }] = useDisclosure()
  const queryClient = useQueryClient()
  const logout = useMutation({
    mutationFn: () => api('/api/auth/logout', { method: 'POST' }),
    onSuccess: () => {
      queryClient.setQueryData(meKey, null) // guard sends us to /login
      queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== meKey[0] }) // drop other users' cached data
    },
  })

  if (me.isPending)
    return (
      <Center mih="100vh">
        <Loader />
      </Center>
    )
  if (!me.data) return <Navigate to="/login" replace state={{ from: location.pathname }} />

  return (
    <AppShell header={{ height: 56 }} navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: !opened } }} padding="md">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between">
          <Group>
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
            <Title order={4}>RekruTime</Title>
          </Group>
          <Group>
            <Text size="sm" visibleFrom="xs">
              {me.data.displayName}
            </Text>
            <Button variant="subtle" size="xs" onClick={() => logout.mutate()} loading={logout.isPending}>
              Log out
            </Button>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        {nav
          .filter((n) => !n.admin || isAdmin(me.data))
          .map((n) => (
            <NavLink key={n.to} component={RouterLink} to={n.to} label={n.label} onClick={close} />
          ))}
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}

export function RequireAdmin() {
  const me = useMe()
  return isAdmin(me.data) ? <Outlet /> : <Navigate to="/" replace />
}
