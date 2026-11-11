import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiPost } from '../apiClient'
import { useAppContext } from '../context/AppContext'
import styles from './AdminLoginPage.module.css'

// ---------------------------------------------------------------------------
// AdminLoginPage (Requirements 8.2, 8.3, 8.4, 8.5, 8.8)
// ---------------------------------------------------------------------------

/** API path for admin authentication (design: POST /api/auth/login). */
const LOGIN_ENDPOINT = '/api/auth/login'

/** Field length limits (Requirement 8.1). */
const LIMITS = {
  username: 254,
  password: 128,
} as const

/** Generic credential-failure copy — no field-level disclosure (Requirement 8.4). */
const INVALID_CREDENTIALS_MESSAGE = 'Invalid username or password.'

/** Service-unavailable copy (Requirement 8.8). */
const SERVICE_UNAVAILABLE_MESSAGE =
  'The service is temporarily unavailable. Please try again later.'

/** Success response shape from POST /api/auth/login (200 OK). */
interface LoginSuccessResponse {
  token: string
}

/** Error response shape for 401 / 429 responses. */
interface LoginErrorResponse {
  error?: string
}

/** The two controlled fields in the login form. */
interface LoginFields {
  username: string
  password: string
}

/**
 * Builds the lockout message for a 429 response (Requirement 8.5).
 *
 * The backend returns `{ error: "Account locked. Try again after <ISO>." }`.
 * We surface that message directly so the unlock time is shown. If the ISO
 * timestamp can be parsed we reformat it into a friendlier local string; if
 * the body is missing or unparseable we fall back to the raw error text or a
 * sensible default so the user is never left without a lockout indication.
 */
function buildLockedMessage(body: LoginErrorResponse | null): string {
  const raw = body?.error?.trim()

  // Attempt to extract an ISO timestamp from the message and reformat it.
  if (raw) {
    const match = raw.match(/(\d{4}-\d{2}-\d{2}T[\d:.]+Z)/)
    if (match) {
      const parsed = new Date(match[1])
      if (!Number.isNaN(parsed.getTime())) {
        return `Your account is temporarily locked. You can try again after ${parsed.toLocaleString()}.`
      }
    }
    // Could not parse a timestamp — surface the server's message verbatim.
    return raw
  }

  return 'Your account is temporarily locked due to too many failed attempts. Please try again later.'
}

const EMPTY_FIELDS: LoginFields = { username: '', password: '' }

/** Discriminated banner state shown above the form. */
interface Banner {
  heading: string
  text: string
}

/**
 * AdminLoginPage renders the `/admin/login` screen with controlled
 * username/password fields and submission handling against the auth API.
 *
 * Behaviour:
 * - On a 200 success the JWT is stored (via AppContext, which persists it to
 *   the `authToken` localStorage key read by ProtectedRoute) and the user is
 *   navigated to `/admin` (Requirements 8.2, 8.3).
 * - On a 401 a generic "Invalid username or password." message is shown with
 *   no field-level disclosure (Requirement 8.4).
 * - On a 429 a lockout message including the unlock time is shown
 *   (Requirement 8.5).
 * - On any other failure (network error, timeout, or non-401/429 status) a
 *   "service temporarily unavailable" message is shown and access is not
 *   granted (Requirement 8.8).
 */
export default function AdminLoginPage() {
  const navigate = useNavigate()
  const { setAuthToken } = useAppContext()

  const [fields, setFields] = useState<LoginFields>(EMPTY_FIELDS)
  const [isLoading, setIsLoading] = useState(false)
  const [banner, setBanner] = useState<Banner | null>(null)

  function handleChange(field: keyof LoginFields) {
    return (event: React.ChangeEvent<HTMLInputElement>) => {
      const value = event.target.value
      setFields((prev) => ({ ...prev, [field]: value }))
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (isLoading) return

    // Clear any previous banner before the new attempt.
    setBanner(null)
    setIsLoading(true)

    const response = await apiPost<LoginSuccessResponse>(LOGIN_ENDPOINT, {
      username: fields.username,
      password: fields.password,
    })

    // ── Success (Requirements 8.2, 8.3) ──────────────────────────────────────
    if (response.ok && response.data?.token) {
      setAuthToken(response.data.token)
      navigate('/admin')
      return
    }

    // ── Invalid credentials (Requirement 8.4) ────────────────────────────────
    if (response.status === 401) {
      setBanner({ heading: 'Login failed', text: INVALID_CREDENTIALS_MESSAGE })
      setIsLoading(false)
      return
    }

    // ── Account locked (Requirement 8.5) ─────────────────────────────────────
    if (response.status === 429) {
      const body = response.data as LoginErrorResponse | null
      setBanner({ heading: 'Account locked', text: buildLockedMessage(body) })
      setIsLoading(false)
      return
    }

    // ── Service unavailable: network error, timeout, or any other status
    //    (Requirement 8.8). Access is not granted. ───────────────────────────
    setBanner({ heading: 'Service unavailable', text: SERVICE_UNAVAILABLE_MESSAGE })
    setIsLoading(false)
  }

  return (
    <main className={styles.page}>
      <div className={styles.card}>
        <h1 className={styles.heading}>Admin Login</h1>

        <form className={styles.form} onSubmit={handleSubmit} noValidate>
          {banner && (
            <div className={styles.errorBanner} role="alert">
              <strong className={styles.errorBannerHeading}>{banner.heading}</strong>
              <span>{banner.text}</span>
            </div>
          )}

          <div className={styles.field}>
            <label className={styles.label} htmlFor="admin-username">
              Username
            </label>
            <input
              id="admin-username"
              name="username"
              type="text"
              autoComplete="username"
              className={styles.input}
              value={fields.username}
              maxLength={LIMITS.username}
              onChange={handleChange('username')}
              disabled={isLoading}
            />
          </div>

          <div className={styles.field}>
            <label className={styles.label} htmlFor="admin-password">
              Password
            </label>
            <input
              id="admin-password"
              name="password"
              type="password"
              autoComplete="current-password"
              className={styles.input}
              value={fields.password}
              maxLength={LIMITS.password}
              onChange={handleChange('password')}
              disabled={isLoading}
            />
          </div>

          <button type="submit" className={styles.submit} disabled={isLoading}>
            {isLoading ? 'Signing in…' : 'Sign In'}
          </button>
        </form>
      </div>
    </main>
  )
}
