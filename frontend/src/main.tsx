import '@mantine/core/styles.css'
import '@mantine/notifications/styles.css'
import { MantineProvider } from '@mantine/core'
import { Notifications } from '@mantine/notifications'
import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { ApiError, basePath } from './api'
import { meKey } from './auth'
import { Home, Layout, RequireAdmin, RequireReviewer } from './Layout'
import { EmailPage } from './pages/admin/EmailPage'
import { FormBuilderPage } from './pages/admin/FormBuilderPage'
import { FormsPage } from './pages/admin/FormsPage'
import { ImportPage } from './pages/admin/ImportPage'
import { LinksPage } from './pages/admin/LinksPage'
import { OverviewPage } from './pages/admin/OverviewPage'
import { RecruitmentPage } from './pages/admin/RecruitmentPage'
import { SectionsPage } from './pages/admin/SectionsPage'
import { UsersPage } from './pages/admin/UsersPage'
import { AvailabilityPage } from './pages/AvailabilityPage'
import { BookingPage } from './pages/BookingPage'
import { FormPage } from './pages/FormPage'
import { InterviewPage } from './pages/InterviewPage'
import { InterviewsPage } from './pages/InterviewsPage'
import { LoginPage } from './pages/LoginPage'
import { RecruitPage } from './pages/RecruitPage'
import { RecruitsPage } from './pages/RecruitsPage'
import { SetPasswordPage } from './pages/SetPasswordPage'
import { theme } from './theme'

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
  { path: '/book/:token', element: <BookingPage /> }, // public: outside the login guard
  { path: '/form/:id', element: <FormPage /> }, // public recruitment form
  { path: '/set-password', element: <SetPasswordPage /> }, // public: link from the account e-mail
  {
    element: <Layout />,
    children: [
      { index: true, element: <Home /> },
      { element: <RequireAdmin />, children: [{ path: 'overview', element: <OverviewPage /> }] },
      { path: 'availability', element: <AvailabilityPage /> },
      { path: 'interviews', element: <InterviewsPage /> },
      { path: 'interviews/:id', element: <InterviewPage /> },
      {
        element: <RequireReviewer />,
        children: [
          { path: 'recruits', element: <RecruitsPage /> },
          { path: 'recruits/:id', element: <RecruitPage /> },
        ],
      },
      {
        path: 'admin',
        element: <RequireAdmin />,
        children: [
          { path: 'recruitment', element: <RecruitmentPage /> },
          { path: 'forms', element: <FormsPage /> },
          { path: 'forms/:id', element: <FormBuilderPage /> },
          { path: 'import', element: <ImportPage /> },
          { path: 'links', element: <LinksPage /> },
          { path: 'email', element: <EmailPage /> },
          { path: 'users', element: <UsersPage /> },
          { path: 'sections', element: <SectionsPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
], { basename: basePath || '/' })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <Notifications position="top-right" />
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
)
