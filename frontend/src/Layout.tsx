import { ActionIcon, AppShell, Avatar, Burger, Center, Group, Loader, Menu, NavLink, ScrollArea, Stack, Text, Tooltip, UnstyledButton, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import {
  IconCalendarEvent,
  IconCalendarTime,
  IconClockHour4,
  IconFileImport,
  IconHierarchy2,
  IconLayoutDashboard,
  IconLink,
  IconLogout,
  IconMail,
  IconMoon,
  IconSun,
  IconUsers,
  type Icon,
} from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Navigate, Outlet, NavLink as RouterLink, useLocation } from 'react-router'
import { api } from './api'
import { isAdmin, meKey, useMe } from './auth'
import { Logo } from './components/Logo'

type Item = { to: string; label: string; icon: Icon }
const nav: { label?: string; admin?: boolean; items: Item[] }[] = [
  {
    items: [
      { to: '/overview', label: 'Overview', icon: IconLayoutDashboard },
      { to: '/interviews', label: 'Interviews', icon: IconCalendarEvent },
      { to: '/availability', label: 'My availability', icon: IconClockHour4 },
    ],
  },
  {
    label: 'Recruitment',
    admin: true,
    items: [
      { to: '/admin/recruitment', label: 'Recruitment period', icon: IconCalendarTime },
      { to: '/admin/import', label: 'Import recruits', icon: IconFileImport },
      { to: '/admin/links', label: 'Booking links', icon: IconLink },
      { to: '/admin/email', label: 'E-mail', icon: IconMail },
    ],
  },
  {
    label: 'Club',
    admin: true,
    items: [
      { to: '/admin/users', label: 'Users', icon: IconUsers },
      { to: '/admin/sections', label: 'Sections', icon: IconHierarchy2 },
    ],
  },
]

const initials = (name: string) =>
  name
    .split(/\s+/)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()

// Guard + shell for every logged-in page.
export function Layout() {
  const me = useMe()
  const location = useLocation()
  const [opened, { toggle, close }] = useDisclosure()
  const { setColorScheme } = useMantineColorScheme()
  const dark = useComputedColorScheme('light') === 'dark'
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
  const admin = isAdmin(me.data)

  return (
    <AppShell
      header={{ height: 60 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="lg"
      styles={{ header: { background: 'var(--mantine-color-default)' }, navbar: { background: 'var(--mantine-color-default)' } }}
    >
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between">
          <Group gap="sm">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Toggle navigation" />
            <Logo />
          </Group>
          <Group gap="xs">
            <Tooltip label={dark ? 'Light mode' : 'Dark mode'}>
              <ActionIcon variant="subtle" color="gray" size="lg" aria-label="Toggle colour scheme" onClick={() => setColorScheme(dark ? 'light' : 'dark')}>
                {dark ? <IconSun size={18} /> : <IconMoon size={18} />}
              </ActionIcon>
            </Tooltip>
            <Menu position="bottom-end" width={200}>
              <Menu.Target>
                <UnstyledButton aria-label="Account">
                  <Group gap="xs">
                    <Avatar color="signal" radius="xl" size={32}>
                      {initials(me.data.displayName)}
                    </Avatar>
                    <Text size="sm" fw={500} visibleFrom="xs">
                      {me.data.displayName}
                    </Text>
                  </Group>
                </UnstyledButton>
              </Menu.Target>
              <Menu.Dropdown>
                <Menu.Label>{me.data.email}</Menu.Label>
                <Menu.Item leftSection={<IconLogout size={16} />} onClick={() => logout.mutate()}>
                  Log out
                </Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="sm">
        <ScrollArea>
          <Stack gap="md">
            {nav
              .filter((g) => !g.admin || admin)
              .map((g, i) => (
                <Stack key={i} gap={2}>
                  {g.label && (
                    <Text size="xs" fw={600} c="dimmed" px="sm" pb={4}>
                      {g.label}
                    </Text>
                  )}
                  {g.items
                    .filter((n) => admin || n.to !== '/overview')
                    .map((n) => (
                      <NavLink key={n.to} component={RouterLink} to={n.to} label={n.label} leftSection={<n.icon size={18} stroke={1.6} />} onClick={close} style={{ borderRadius: 'var(--mantine-radius-md)' }} />
                    ))}
                </Stack>
              ))}
          </Stack>
        </ScrollArea>
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

// Admins land on the overview, recruiters on their interviews.
export function Home() {
  const me = useMe()
  return <Navigate to={isAdmin(me.data) ? '/overview' : '/interviews'} replace />
}
