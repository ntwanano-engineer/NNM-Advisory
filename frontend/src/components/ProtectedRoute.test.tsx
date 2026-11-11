import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ProtectedRoute from './ProtectedRoute'
import { AppProvider } from '../context/AppContext'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Creates a minimal JWT with the given `exp` (Unix seconds). */
function makeJwt(exp: number): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '')
  const payload = btoa(JSON.stringify({ sub: 'admin-id', username: 'admin', exp }))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '')
  const signature = 'fakesig'
  return `${header}.${payload}.${signature}`
}

const futureExp = Math.floor(Date.now() / 1000) + 3600   // 1 hour from now
const pastExp   = Math.floor(Date.now() / 1000) - 3600   // 1 hour ago

// ---------------------------------------------------------------------------
// Test setup: render helper that wraps with AppProvider and MemoryRouter,
// placing the protected route at /admin and a login stub at /admin/login.
// ---------------------------------------------------------------------------

function renderWithRouter(initialPath = '/admin') {
  return render(
    <AppProvider>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route
            path="/admin"
            element={
              <ProtectedRoute>
                <div>Admin Dashboard</div>
              </ProtectedRoute>
            }
          />
          <Route path="/admin/login" element={<div>Login Page</div>} />
        </Routes>
      </MemoryRouter>
    </AppProvider>
  )
}

// ---------------------------------------------------------------------------
// Tests — Requirements 8.1, 8.2
// ---------------------------------------------------------------------------

describe('ProtectedRoute', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('redirects to /admin/login when no token is present in localStorage', () => {
    renderWithRouter('/admin')

    expect(screen.getByText('Login Page')).toBeInTheDocument()
    expect(screen.queryByText('Admin Dashboard')).not.toBeInTheDocument()
  })

  it('redirects to /admin/login when token is expired', () => {
    localStorage.setItem('nnm_auth_token', makeJwt(pastExp))

    renderWithRouter('/admin')

    expect(screen.getByText('Login Page')).toBeInTheDocument()
    expect(screen.queryByText('Admin Dashboard')).not.toBeInTheDocument()
  })

  it('renders children when a valid (non-expired) token is present', () => {
    localStorage.setItem('nnm_auth_token', makeJwt(futureExp))

    renderWithRouter('/admin')

    expect(screen.getByText('Admin Dashboard')).toBeInTheDocument()
    expect(screen.queryByText('Login Page')).not.toBeInTheDocument()
  })

  it('redirects to /admin/login when the token is malformed (not a valid JWT)', () => {
    localStorage.setItem('nnm_auth_token', 'not.a.valid.jwt.at.all')

    renderWithRouter('/admin')

    expect(screen.getByText('Login Page')).toBeInTheDocument()
    expect(screen.queryByText('Admin Dashboard')).not.toBeInTheDocument()
  })

  it('redirects to /admin/login when the token has no exp claim', () => {
    const header  = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
      .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '')
    const payload = btoa(JSON.stringify({ sub: 'admin-id', username: 'admin' }))  // no exp
      .replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '')
    localStorage.setItem('nnm_auth_token', `${header}.${payload}.fakesig`)

    renderWithRouter('/admin')

    expect(screen.getByText('Login Page')).toBeInTheDocument()
  })
})

