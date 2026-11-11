/**
 * apiClient.test.ts
 *
 * Unit tests for the apiClient module covering:
 *  - Base URL construction
 *  - Authorization header injection
 *  - 30-second timeout behaviour
 *  - HTTP status → user-facing message mapping
 *  - Token storage helpers
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  apiRequest,
  apiGet,
  apiPost,
  apiPut,
  HTTP_ERROR_MESSAGES,
  TIMEOUT_ERROR_MESSAGE,
  GENERIC_ERROR_MESSAGE,
  getStoredToken,
  setStoredToken,
  clearStoredToken,
} from './apiClient'

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Build a minimal Response-like object for fetch mocking */
function makeResponse(
  status: number,
  body: unknown = null,
  contentType = 'application/json',
): Response {
  const json = body !== null ? JSON.stringify(body) : ''
  return new Response(json || null, {
    status,
    headers: { 'Content-Type': contentType },
  })
}

// ─── Setup ───────────────────────────────────────────────────────────────────

beforeEach(() => {
  // Clear localStorage before every test
  localStorage.clear()
  vi.clearAllMocks()
})

afterEach(() => {
  vi.restoreAllMocks()
})

// ─── Base URL ─────────────────────────────────────────────────────────────────

describe('base URL construction', () => {
  it('prepends VITE_API_URL to the supplied path', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, { ok: true }))

    await apiRequest('/api/profile', { token: null })

    expect(fetchSpy).toHaveBeenCalledOnce()
    const calledUrl = (fetchSpy.mock.calls[0] as [string, ...unknown[]])[0]
    // VITE_API_URL is not set in the test environment so BASE_URL is ''
    expect(calledUrl).toBe('/api/profile')
  })
})

// ─── Authorization header ─────────────────────────────────────────────────────

describe('Authorization header injection', () => {
  it('injects Bearer token from localStorage when present', async () => {
    localStorage.setItem('nnm_auth_token', 'test-token-abc')

    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, {}))

    await apiRequest('/api/admin/profile')

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBe(
      'Bearer test-token-abc',
    )
  })

  it('does NOT inject Authorization header when localStorage has no token', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, {}))

    await apiRequest('/api/profile', { token: null })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBeUndefined()
  })

  it('uses an explicitly provided token override', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, {}))

    await apiRequest('/api/admin/profile', { token: 'explicit-token-xyz' })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBe(
      'Bearer explicit-token-xyz',
    )
  })

  it('suppresses the header when token override is null even if localStorage has a value', async () => {
    localStorage.setItem('nnm_auth_token', 'stored-token')

    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, {}))

    await apiRequest('/api/profile', { token: null })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBeUndefined()
  })
})

// ─── 30-second timeout ────────────────────────────────────────────────────────

describe('30-second AbortController timeout', () => {
  it('returns timedOut=true and the timeout error message when fetch rejects with AbortError', async () => {
    // The production code calls controller.abort() after 30 s which causes fetch to
    // reject with an AbortError.  We simulate that rejection directly here — the
    // timer behaviour (30 s threshold) is covered by the integration test.
    vi.spyOn(global, 'fetch').mockRejectedValue(
      new DOMException('The operation was aborted.', 'AbortError'),
    )

    const result = await apiRequest('/api/contact', { token: null })

    expect(result.ok).toBe(false)
    expect(result.timedOut).toBe(true)
    expect(result.errorMessage).toBe(TIMEOUT_ERROR_MESSAGE)
    expect(result.status).toBe(0)
  })

  it('returns ok=true for a fast response (well within 30 seconds)', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue(makeResponse(202, { message: 'Received' }))

    const result = await apiRequest('/api/contact', {
      method: 'POST',
      body: { fullName: 'Jane' },
      token: null,
    })

    expect(result.ok).toBe(true)
    expect(result.timedOut).toBe(false)
    expect(result.status).toBe(202)
  })
})

// ─── HTTP status → error message mapping ─────────────────────────────────────

describe('HTTP status to user-facing message mapping', () => {
  const cases: [number, string][] = [
    [400, HTTP_ERROR_MESSAGES[400]],
    [401, HTTP_ERROR_MESSAGES[401]],
    [403, HTTP_ERROR_MESSAGES[403]],
    [422, HTTP_ERROR_MESSAGES[422]],
    [429, HTTP_ERROR_MESSAGES[429]],
    [500, HTTP_ERROR_MESSAGES[500]],
  ]

  it.each(cases)(
    'maps HTTP %i to the correct user-facing message',
    async (status, expectedMessage) => {
      vi.spyOn(global, 'fetch').mockResolvedValue(makeResponse(status, { error: 'raw' }))

      const result = await apiRequest('/api/test', { token: null })

      expect(result.ok).toBe(false)
      expect(result.status).toBe(status)
      expect(result.errorMessage).toBe(expectedMessage)
    },
  )

  it('falls back to a generic message for unmapped status codes', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue(makeResponse(503, null, 'text/plain'))

    const result = await apiRequest('/api/test', { token: null })

    expect(result.ok).toBe(false)
    expect(result.errorMessage).toBe(GENERIC_ERROR_MESSAGE)
  })

  it('returns ok=true and null errorMessage on a 2xx response', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue(makeResponse(200, { data: 'content' }))

    const result = await apiRequest('/api/profile', { token: null })

    expect(result.ok).toBe(true)
    expect(result.errorMessage).toBeNull()
    expect(result.data).toEqual({ data: 'content' })
  })
})

// ─── Network failure ──────────────────────────────────────────────────────────

describe('network failure handling', () => {
  it('returns ok=false with generic message when fetch throws a non-abort error', async () => {
    vi.spyOn(global, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    const result = await apiRequest('/api/profile', { token: null })

    expect(result.ok).toBe(false)
    expect(result.timedOut).toBe(false)
    expect(result.errorMessage).toBe(GENERIC_ERROR_MESSAGE)
    expect(result.status).toBe(0)
  })
})

// ─── Convenience helpers ──────────────────────────────────────────────────────

describe('convenience helpers (apiGet, apiPost, apiPut)', () => {
  it('apiGet issues a GET request', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, []))

    await apiGet('/api/profile/services', { token: null })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('GET')
  })

  it('apiPost issues a POST request with a JSON body', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(202, { message: 'ok' }))

    const payload = { fullName: 'Jane', email: 'jane@example.com' }
    await apiPost('/api/contact', payload, { token: null })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify(payload))
  })

  it('apiPut issues a PUT request with a JSON body', async () => {
    const fetchSpy = vi
      .spyOn(global, 'fetch')
      .mockResolvedValue(makeResponse(200, {}))

    const payload = { tagline: 'New tagline' }
    await apiPut('/api/admin/profile', payload, { token: 'admin-token' })

    const [, init] = fetchSpy.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer admin-token')
    expect(init.body).toBe(JSON.stringify(payload))
  })
})

// ─── Token storage helpers ────────────────────────────────────────────────────

describe('token storage helpers', () => {
  it('setStoredToken persists the token, getStoredToken retrieves it', () => {
    setStoredToken('my-jwt')
    expect(getStoredToken()).toBe('my-jwt')
  })

  it('clearStoredToken removes the token', () => {
    setStoredToken('my-jwt')
    clearStoredToken()
    expect(getStoredToken()).toBeNull()
  })

  it('getStoredToken returns null when nothing has been stored', () => {
    expect(getStoredToken()).toBeNull()
  })
})
