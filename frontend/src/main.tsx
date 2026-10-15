import '@mantine/core/styles.css'
import { MantineProvider } from '@mantine/core'
import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { ApiError } from './api'
import { meKey } from './auth'
import { Layout, RequireAdmin, Todo } from './Layout'
import { SectionsPage } from './pages/admin/SectionsPage'
import { UsersPage } from './pages/admin/UsersPage'
import { LoginPage } from './pages/LoginPage'

const queryClient: QueryClient = new QueryClient({
  // Session expired mid-use: any 401 logs the user out → guard redirects to /login.
  queryCache: new QueryCache({
    onError: (e) => {
      if (e instanceof ApiError && e.status === 401) queryClient.setQueryData(meKey, null)
    },
  }),
  defaultOptions: { queries: { retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 2 } },
})

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/interviews" replace /> },
      { path: 'availability', element: <Todo title="My availability" /> },
      { path: 'interviews', element: <Todo title="Interviews" /> },
      {
        path: 'admin',
        element: <RequireAdmin />,
        children: [
          { path: 'links', element: <Todo title="Booking links" /> },
          { path: 'users', element: <UsersPage /> },
          { path: 'sections', element: <SectionsPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider defaultColorScheme="auto">
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
)
