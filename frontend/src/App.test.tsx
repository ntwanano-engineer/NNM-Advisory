import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AppProvider } from './context/AppContext'

// Import App's route configuration indirectly by rendering individual routes
// via MemoryRouter so we can control the initial URL in each test.
// We re-export App using MemoryRouter for routing tests to avoid BrowserRouter
// conflicts with jsdom.
import { lazy, Suspense } from 'react'
import { Routes, Route } from 'react-router-dom'
import ProtectedRoute from './components/ProtectedRoute'

const HomePage           = lazy(() => import('./pages/HomePage'))
const NotFoundPage       = lazy(() => import('./pages/NotFoundPage'))
const AdminDashboardPage = lazy(() => import('./pages/AdminDashboardPage'))
const AdminLoginPage     = lazy(() => import('./pages/AdminLoginPage'))

/** Minimal router wrapper that mirrors App's route table for the paths under test. */
function TestApp({ initialPath }: { initialPath: string }) {
  return (
    <AppProvider>
      <MemoryRouter initialEntries={[initialPath]}>
        <Suspense fallback={<div>Loading…</div>}>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/admin/login" element={<AdminLoginPage />} />
            <Route
              path="/admin"
              element={
                <ProtectedRoute>
                  <AdminDashboardPage />
                </ProtectedRoute>
              }
            />
            <Route path="*" element={<NotFoundPage />} />
          </Routes>
        </Suspense>
      </MemoryRouter>
    </AppProvider>
  )
}

// ---------------------------------------------------------------------------
// Tests — Requirements 1.1, 1.2, 1.5, 8.1, 8.2
// ---------------------------------------------------------------------------

describe('App routing', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('renders the Home page at /', async () => {
    render(<TestApp initialPath="/" />)
    await waitFor(() => expect(screen.getByRole('heading', { name: /nnm advisory/i })).toBeInTheDocument())
  })

  it('renders the 404 Not Found page for an unknown route', async () => {
    render(<TestApp initialPath="/this-does-not-exist" />)
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: /404/i })).toBeInTheDocument()
    )
  })

  it('the 404 page contains a link back to Home', async () => {
    render(<TestApp initialPath="/this-does-not-exist" />)
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /back to home/i })
      expect(link).toBeInTheDocument()
      expect(link).toHaveAttribute('href', '/')
    })
  })

  it('redirects unauthenticated access to /admin → /admin/login', async () => {
    // No token in localStorage
    render(<TestApp initialPath="/admin" />)
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: /admin login/i })).toBeInTheDocument()
    )
  })
})

