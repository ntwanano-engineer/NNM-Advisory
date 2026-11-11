import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import AdminContentEditor, { EDITOR_LIMITS } from './AdminContentEditor'

// ---------------------------------------------------------------------------
// Tests — Requirement 9.6
//
// AdminContentEditor is a config-driven editor with:
//   - a debounced live preview (PREVIEW_DEBOUNCE_MS = 500) that reflects input
//     within ~1 second of the admin ceasing to type
//   - per-field character counters that gain over-limit styling (and render
//     an "over the limit" hint) when a field exceeds its limit
//   - a Save button disabled when any field is over its limit or empty, and a
//     save that PUTs to the correct admin endpoint via apiPut
//
// These tests exercise the leadership/biography variant (limit 2000):
//   • typing updates the live preview (after the debounce)              Req 9.6
//   • a > 2000-char biography flags the counter and disables Save        Req 9.6
//   • a valid (≤ 2000) biography keeps Save enabled and PUTs on click    Req 9.6
// ---------------------------------------------------------------------------

const apiPut = vi.fn()
vi.mock('../apiClient', () => ({
  apiPut: (...args: unknown[]) => apiPut(...args),
}))

function successResponse() {
  return { ok: true, status: 200, data: null, errorMessage: null, timedOut: false }
}

const leadershipTarget = {
  kind: 'leadership' as const,
  id: 'leader-123',
  name: 'Ntwanano Mabasa',
}

describe('AdminContentEditor', () => {
  beforeEach(() => {
    apiPut.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('updates the live preview within ~1s of typing a biography (Req 9.6)', () => {
    vi.useFakeTimers()

    render(
      <AdminContentEditor
        target={leadershipTarget}
        initialValues={{ biography: '' }}
      />,
    )

    const preview = screen.getByRole('region', { name: 'Live preview' })
    // Nothing typed yet → the empty placeholder is shown in the preview.
    expect(within(preview).getByText('Nothing to preview yet.')).toBeInTheDocument()

    // fireEvent is used instead of userEvent here because userEvent's async
    // waits do not interoperate cleanly with fake timers; a direct change
    // event lets us control the debounce clock deterministically.
    fireEvent.change(screen.getByLabelText('Biography'), {
      target: { value: 'A seasoned advisor.' },
    })

    // Before the debounce elapses the preview has not settled on the new text.
    expect(
      within(preview).queryByText('A seasoned advisor.'),
    ).not.toBeInTheDocument()

    // Advance past the 500ms debounce (well within the 1 second budget).
    act(() => {
      vi.advanceTimersByTime(500)
    })

    expect(
      within(preview).getByText('A seasoned advisor.'),
    ).toBeInTheDocument()
  })

  it('flags the counter and disables Save when the biography exceeds 2000 chars (Req 9.6)', async () => {
    const user = userEvent.setup()

    const overLimit = 'x'.repeat(EDITOR_LIMITS.biography + 1)
    render(
      <AdminContentEditor
        target={leadershipTarget}
        initialValues={{ biography: overLimit }}
      />,
    )

    // The counter renders the over-limit hint and the over-limit class.
    const counter = screen.getByText(/over the limit/i)
    expect(counter).toBeInTheDocument()
    expect(counter.className).toContain('counterOver')

    // Save is disabled while a field is over its limit.
    expect(
      screen.getByRole('button', { name: 'Save changes' }),
    ).toBeDisabled()

    // Removing a character brings it back within the limit and re-enables Save.
    const textarea = screen.getByLabelText('Biography')
    await user.type(textarea, '{backspace}')

    expect(screen.queryByText(/over the limit/i)).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Save changes' }),
    ).toBeEnabled()
  })

  it('keeps Save enabled for a valid biography and PUTs the leadership endpoint on click (Req 9.6)', async () => {
    const user = userEvent.setup()
    apiPut.mockResolvedValue(successResponse())
    const onSaved = vi.fn()

    render(
      <AdminContentEditor
        target={leadershipTarget}
        initialValues={{ biography: 'An experienced leader.' }}
        onSaved={onSaved}
      />,
    )

    const saveButton = screen.getByRole('button', { name: 'Save changes' })
    expect(saveButton).toBeEnabled()
    expect(screen.queryByText(/over the limit/i)).not.toBeInTheDocument()

    await user.click(saveButton)

    await waitFor(() => expect(apiPut).toHaveBeenCalledTimes(1))
    expect(apiPut).toHaveBeenCalledWith('/api/admin/leadership/leader-123', {
      biography: 'An experienced leader.',
    })
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
  })
})
