import { ActionIcon, Anchor, AppShell, Avatar, Burger, Center, Group, Loader, Menu, Modal, NavLink, Paper, ScrollArea, Stack, Text, Title, Tooltip, UnstyledButton, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import {
  IconCalendarEvent,
  IconCalendarTime,
  IconClockHour4,
  IconFileImport,
  IconFilter,
  IconForms,
  IconHierarchy2,
  IconKey,
  IconLayoutDashboard,
  IconLink,
  IconLogout,
  IconMail,
  IconMoon,
  IconSun,
  IconUserCheck,
  IconUsers,
  type Icon,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, Outlet, NavLink as RouterLink, useLocation } from 'react-router'
import { api } from './api'
import { isAdmin, meKey, useMe } from './auth'
import { isReviewer } from './recruits'
import { ChangePasswordForm } from './components/ChangePasswordForm'
import { Logo } from './components/Logo'

type Item = { to: string; label: string; icon: Icon }
const nav: { label?: string; admin?: boolean; items: Item[] }[] = [
  {
    items: [
      { to: '/overview', label: 'Overview', icon: IconLayoutDashboard },
      { to: '/interviews', label: 'Interviews', icon: IconCalendarEvent },
      { to: '/preselection', label: 'Preselection', icon: IconFilter },
      { to: '/recruits', label: 'Recruits', icon: IconUserCheck },
      { to: '/availability', label: 'My availability', icon: IconClockHour4 },
    ],
  },
  {
    label: 'Recruitment',
    admin: true,
    items: [
      { to: '/admin/recruitment', label: 'Recruitment period', icon: IconCalendarTime },
      { to: '/admin/forms', label: 'Forms', icon: IconForms },
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
  const [changingPassword, setChangingPassword] = useState(false)
  const { setColorScheme } = useMantineColorScheme()
  const dark = useComputedColorScheme('light') === 'dark'
  const queryClient = useQueryClient()
  // Which release runs here ("dev" outside the Docker image) – in the account menu.
  const version = useQuery({ queryKey: ['health'], queryFn: () => api<{ version: string }>('/api/health'), staleTime: Infinity }).data?.version
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

  // Temporary password from an admin → choose your own before anything else.
  if (me.data.mustChangePassword)
    return (
      <Center mih="100vh" p="md">
        <Paper withBorder shadow="sm" p="xl" w="100%" maw={420}>
          <Stack>
            <Logo size={28} />
            <Title order={3}>Set your own password</Title>
            <Text size="sm" c="dimmed">
              You logged in with a temporary password from an admin. Choose your own to continue – the current password is the temporary one.
            </Text>
            <ChangePasswordForm submitLabel="Save and continue" />
            <Anchor component="button" size="sm" c="dimmed" onClick={() => logout.mutate()}>
              Log out
            </Anchor>
          </Stack>
        </Paper>
      </Center>
    )

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
                <Menu.Item leftSection={<IconKey size={16} />} onClick={() => setChangingPassword(true)}>
                  Change password
                </Menu.Item>
                <Menu.Item leftSection={<IconLogout size={16} />} onClick={() => logout.mutate()}>
                  Log out
                </Menu.Item>
                {version && (
                  <Text size="xs" c="dimmed" px="sm" pt={4} pb={2}>
                    RekruTime {version}
                  </Text>
                )}
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
                    .filter((n) => (n.to === '/overview' ? admin : n.to === '/recruits' || n.to === '/preselection' ? isReviewer(me.data) : true))
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
      <Modal opened={changingPassword} onClose={() => setChangingPassword(false)} title="Change password" centered>
        <ChangePasswordForm onDone={() => setChangingPassword(false)} />
      </Modal>
    </AppShell>
  )
}

export function RequireAdmin() {
  const me = useMe()
  return isAdmin(me.data) ? <Outlet /> : <Navigate to="/" replace />
}

// Admins and section leaders.
export function RequireReviewer() {
  const me = useMe()
  return isReviewer(me.data) ? <Outlet /> : <Navigate to="/" replace />
}

// Admins land on the overview, recruiters on their interviews.
export function Home() {
  const me = useMe()
  return <Navigate to={isAdmin(me.data) ? '/overview' : '/interviews'} replace />
}
