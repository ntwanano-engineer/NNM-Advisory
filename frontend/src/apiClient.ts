/**
 * apiClient.ts
 *
 * Thin fetch wrapper providing:
 *  - Base URL from VITE_API_URL environment variable
 *  - Authorization: Bearer <token> header injection (token read from localStorage)
 *  - 30-second AbortController timeout (Requirement 5.10)
 *  - Centralised HTTP status → user-facing message mapping (Requirement 10.2)
 */

import { notifyActivity } from './activityNotifier'

const BASE_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? ''
const TIMEOUT_MS = 30_000
const TOKEN_KEY = 'nnm_auth_token'

// ─── User-facing error messages ───────────────────────────────────────────────

export const HTTP_ERROR_MESSAGES: Record<number, string> = {
  400: 'The request contained invalid data. Please check your input and try again.',
  401: 'Your session has expired or your credentials are invalid. Please log in again.',
  403: 'You do not have permission to perform this action.',
  422: 'Some fields are invalid. Please review your input and try again.',
  429: 'Too many attempts. Your account has been temporarily locked. Please try again later.',
  500: 'Something went wrong on our end. Please try again later.',
}

export const TIMEOUT_ERROR_MESSAGE =
  'The request timed out. Please check your connection and try again.'

export const GENERIC_ERROR_MESSAGE =
  'Something went wrong. Please try again later.'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ApiResponse<T = unknown> {
  ok: boolean
  status: number
  data: T | null
  /** Human-readable error message, set when ok is false */
  errorMessage: string | null
  /** True when the request was aborted due to the 30-second timeout */
  timedOut: boolean
}

export interface ApiRequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  body?: unknown
  /** Override the auth token (useful for testing); pass null to suppress the header */
  token?: string | null
  headers?: Record<string, string>
}

// ─── Core request function ────────────────────────────────────────────────────

/**
 * Performs an authenticated fetch request with a 30-second timeout.
 *
 * @param path   Path relative to VITE_API_URL (e.g. "/api/contact")
 * @param opts   Request options
 */
export async function apiRequest<T = unknown>(
  path: string,
  opts: ApiRequestOptions = {},
): Promise<ApiResponse<T>> {
  const { method = 'GET', body, headers: extraHeaders = {} } = opts

  // Signal API activity so the admin inactivity timer resets (Requirement 8.6).
  // This is purely a side-channel notification and does not affect the request.
  notifyActivity()

  // Resolve auth token: explicit override > localStorage > none
  const token =
    opts.token !== undefined
      ? opts.token
      : (localStorage.getItem(TOKEN_KEY) ?? null)

  // Build headers
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...extraHeaders,
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  // 30-second timeout via AbortController
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), TIMEOUT_MS)

  try {
    const response = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    })

    clearTimeout(timeoutId)

    // Parse JSON body when present; fall back gracefully if empty / non-JSON
    let data: T | null = null
    const contentType = response.headers.get('content-type') ?? ''
    if (contentType.includes('application/json')) {
      try {
        data = (await response.json()) as T
      } catch {
        data = null
      }
    }

    if (response.ok) {
      return { ok: true, status: response.status, data, errorMessage: null, timedOut: false }
    }

    // Map known HTTP status codes to user-facing messages
    const errorMessage =
      HTTP_ERROR_MESSAGES[response.status] ?? GENERIC_ERROR_MESSAGE

    return {
      ok: false,
      status: response.status,
      data,
      errorMessage,
      timedOut: false,
    }
  } catch (err: unknown) {
    clearTimeout(timeoutId)

    // AbortError means the 30-second timeout fired.
    // DOMException does not extend Error in some environments (e.g. jsdom), so we
    // check .name directly rather than combining instanceof Error with the name check.
    if (
      (err instanceof DOMException || err instanceof Error) &&
      (err as { name?: string }).name === 'AbortError'
    ) {
      return {
        ok: false,
        status: 0,
        data: null,
        errorMessage: TIMEOUT_ERROR_MESSAGE,
        timedOut: true,
      }
    }

    // Network failure or other unexpected error
    return {
      ok: false,
      status: 0,
      data: null,
      errorMessage: GENERIC_ERROR_MESSAGE,
      timedOut: false,
    }
  }
}

// ─── Convenience helpers ──────────────────────────────────────────────────────

export const apiGet = <T = unknown>(
  path: string,
  opts?: Omit<ApiRequestOptions, 'method' | 'body'>,
) => apiRequest<T>(path, { ...opts, method: 'GET' })

export const apiPost = <T = unknown>(
  path: string,
  body: unknown,
  opts?: Omit<ApiRequestOptions, 'method' | 'body'>,
) => apiRequest<T>(path, { ...opts, method: 'POST', body })

export const apiPut = <T = unknown>(
  path: string,
  body: unknown,
  opts?: Omit<ApiRequestOptions, 'method' | 'body'>,
) => apiRequest<T>(path, { ...opts, method: 'PUT', body })

/** Retrieve the stored auth token from localStorage */
export const getStoredToken = (): string | null =>
  localStorage.getItem(TOKEN_KEY)

/** Persist an auth token to localStorage */
export const setStoredToken = (token: string): void =>
  localStorage.setItem(TOKEN_KEY, token)

/** Remove the auth token from localStorage (logout / session expiry) */
export const clearStoredToken = (): void =>
  localStorage.removeItem(TOKEN_KEY)
