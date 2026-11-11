import { Navigate } from 'react-router-dom'
import { useAppContext } from '../context/AppContext'

// ---------------------------------------------------------------------------
// JWT helpers
// ---------------------------------------------------------------------------

/**
 * Decodes the payload of a JWT (base64url) without verifying the signature.
 * Signature verification is the backend's responsibility; the frontend only
 * needs the `exp` claim to decide whether to redirect to the login page.
 */
function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const parts = token.split('.')
    if (parts.length !== 3) return null

    // base64url → base64 → decoded JSON
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=')
    const jsonStr = atob(padded)
    return JSON.parse(jsonStr) as Record<string, unknown>
  } catch {
    return null
  }
}

/**
 * Returns true if the token is present and its `exp` claim has not yet passed.
 * An absent or malformed token is treated as expired.
 */
function isTokenValid(token: string | null): boolean {
  if (!token) return false

  const payload = decodeJwtPayload(token)
  if (!payload) return false

  const exp = payload['exp']
  if (typeof exp !== 'number') return false

  // `exp` is Unix time in seconds; Date.now() is in milliseconds
  return Date.now() < exp * 1000
}

// ---------------------------------------------------------------------------
// ProtectedRoute
// ---------------------------------------------------------------------------

interface ProtectedRouteProps {
  children: React.ReactNode
}

/**
 * ProtectedRoute guards admin-only pages.
 *
 * Behaviour (Requirements 8.1, 8.2):
 * - Reads the JWT from AppContext (which is initialised from localStorage).
 * - Decodes the `exp` claim and compares it to the current time.
 * - If the token is absent or expired, redirects to `/admin/login` immediately.
 * - If the token is valid, renders the children.
 *
 * The redirect uses `replace` so the login page does not appear in the
 * browser history, preventing the user from pressing Back to re-enter a
 * protected page.
 */
export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { state } = useAppContext()
  const token = state.authToken ?? localStorage.getItem('nnm_auth_token')

  if (!isTokenValid(token)) {
    return <Navigate to="/admin/login" replace />
  }

  return <>{children}</>
}

