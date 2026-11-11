import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import fc from 'fast-check'
import AdminContentEditor from './AdminContentEditor'

// ---------------------------------------------------------------------------
// Property 8: Admin Content Update Reflected in Subsequent Reads (frontend)
// Validates: Requirements 2.4, 3.6, 9.2
//
// After an admin PUTs updated content, a subsequent public GET must return the
// updated value. We model a tiny in-memory "server" store holding the current
// tagline. The mocked apiPut for PUT /api/admin/profile writes the new tagline
// into the store and resolves 200/ok; the mocked apiGet for GET /api/profile
// returns the current store value as the profile content rows.
//
// For an arbitrary valid tagline (non-blank, ≤ 150 chars) we:
//   1. render AdminContentEditor targeting the tagline,
//   2. replace the field value with the generated tagline and click Save,
//   3. await the PUT resolving (success banner appears),
//   4. perform GET /api/profile via the mocked apiGet,
//   5. assert the returned contentValue equals the value that was PUT.
//
// This proves update-then-read reflects the new value.
// ---------------------------------------------------------------------------

// ─── In-memory "server" store ──────────────────────────────────────────────
// A single mutable variable standing in for the persisted tagline on the
// backend. The mocked PUT writes to it; the mocked GET reads from it.
let serverTagline = 'Initial tagline'

interface ApiEnvelope<T> {
  ok: boolean
  status: number
  data: T | null
  errorMessage: string | null
  timedOut: boolean
}

// ─── Mock the apiClient module the component imports from ───────────────────
// apiPut: PUT /api/admin/profile updates the store from the request body and
//         resolves { ok: true, status: 200 }.
// apiGet: GET /api/profile returns the current store value as the content rows
//         shape the public profile endpoint produces.
vi.mock('../apiClient', () => ({
  apiPut: async (path: string, body: unknown): Promise<ApiEnvelope<unknown>> => {
    if (path === '/api/admin/profile') {
      const tagline = (body as { tagline?: unknown }).tagline
      if (typeof tagline === 'string') {
        serverTagline = tagline
      }
      return { ok: true, status: 200, data: null, errorMessage: null, timedOut: false }
    }
    // Unexpected path in this test — surface as a failure.
    return { ok: false, status: 404, data: null, errorMessage: 'not found', timedOut: false }
  },
  apiGet: async (
    path: string,
  ): Promise<ApiEnvelope<Array<{ contentKey: string; contentValue: string }>>> => {
    if (path === '/api/profile') {
      return {
        ok: true,
        status: 200,
        data: [{ contentKey: 'tagline', contentValue: serverTagline }],
        errorMessage: null,
        timedOut: false,
      }
    }
    return { ok: false, status: 404, data: null, errorMessage: 'not found', timedOut: false }
  },
}))

// Imported after the mock is registered so we get the mocked implementations.
import { apiGet } from '../apiClient'

/** Read the current tagline from the mocked public profile endpoint. */
async function readTaglineFromProfile(): Promise<string | undefined> {
  const res = await apiGet<Array<{ contentKey: string; contentValue: string }>>(
    '/api/profile',
  )
  return res.data?.find((row) => row.contentKey === 'tagline')?.contentValue
}

describe('AdminContentEditor — Property 8: Admin content update reflected in subsequent reads', () => {
  beforeEach(() => {
    serverTagline = 'Initial tagline'
  })

  it('GET /api/profile after PUT /api/admin/profile returns the updated tagline', async () => {
    await fc.assert(
      fc.asyncProperty(
        // Arbitrary valid tagline: non-blank once trimmed, ≤ 150 chars.
        fc
          .string({ minLength: 1, maxLength: 150 })
          .filter((s) => s.trim().length > 0 && s.length <= 150),
        async (newTagline) => {
          // Reset the store for each run so prior runs cannot mask a failure.
          serverTagline = 'Initial tagline'

          render(
            <AdminContentEditor
              target={{ kind: 'tagline' }}
              initialValues={{ tagline: 'Initial tagline' }}
            />,
          )

          // Replace the tagline field value with the generated tagline. Setting
          // the value directly via a change event avoids per-character typing
          // cost across 100 runs while still driving the controlled input.
          const input = screen.getByLabelText('Tagline') as HTMLInputElement
          fireEvent.change(input, { target: { value: newTagline } })

          // Click Save to trigger the (mocked) PUT.
          fireEvent.click(screen.getByRole('button', { name: /save changes/i }))

          // Await the PUT resolving: the success banner confirms onSaved ran.
          await waitFor(() => {
            expect(screen.getByRole('status')).toHaveTextContent(
              /your changes have been saved/i,
            )
          })

          // Subsequent read must reflect the value that was PUT. The editor
          // trims the tagline before sending, so compare against the trimmed
          // value.
          const readValue = await readTaglineFromProfile()
          expect(readValue).toBe(newTagline.trim())

          cleanup()
        },
      ),
      { numRuns: 100 },
    )
  })
})
