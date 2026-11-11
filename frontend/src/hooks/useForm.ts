/**
 * useForm.ts
 *
 * A small hook that wraps {@link apiPost} to manage the submission lifecycle of
 * a form: tracking loading, success, error, and timeout states derived from the
 * `apiClient` response envelope (Requirements 5.8, 5.9, 5.10).
 *
 * The hook deliberately does NOT own field values or client-side validation —
 * those remain the responsibility of the consuming component so that field
 * state and inline validation stay close to the markup. `useForm` is only
 * concerned with the asynchronous submission and its outcome.
 */

import { useCallback, useState } from 'react'
import { apiPost, type ApiResponse } from '../apiClient'

/**
 * The discrete states a submission can be in.
 *
 * - `idle`    — no submission has been attempted (or state was reset).
 * - `loading` — a request is in flight.
 * - `success` — the server returned a 2xx response (`ok === true`).
 * - `error`   — the server returned a non-2xx response (`ok === false`,
 *               `timedOut === false`).
 * - `timeout` — the request was aborted by the 30-second timeout
 *               (`timedOut === true`).
 */
export type FormStatus = 'idle' | 'loading' | 'success' | 'error' | 'timeout'

export interface UseFormResult<TResponse> {
  /** Current submission status. */
  status: FormStatus
  /** User-facing error message, set when `status === 'error' | 'timeout'`. */
  errorMessage: string | null
  /** The full response envelope from the most recent submission, if any. */
  response: ApiResponse<TResponse> | null
  /** `true` while a request is in flight. */
  isLoading: boolean
  /** `true` once the server returned a 2xx response. */
  isSuccess: boolean
  /** `true` when the last submission failed (non-2xx response). */
  isError: boolean
  /** `true` when the last submission timed out after 30 seconds. */
  isTimeout: boolean
  /**
   * POST `body` to `path` and update the submission state accordingly.
   * Returns the response envelope so callers can inspect it if needed.
   */
  submit: (path: string, body: unknown) => Promise<ApiResponse<TResponse>>
  /** Reset the hook back to its `idle` state. */
  reset: () => void
}

/**
 * Hook that drives a form submission through `apiPost` and exposes a small,
 * declarative state machine for the component to render against.
 */
export function useForm<TResponse = unknown>(): UseFormResult<TResponse> {
  const [status, setStatus] = useState<FormStatus>('idle')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [response, setResponse] = useState<ApiResponse<TResponse> | null>(null)

  const submit = useCallback(
    async (path: string, body: unknown): Promise<ApiResponse<TResponse>> => {
      setStatus('loading')
      setErrorMessage(null)

      const res = await apiPost<TResponse>(path, body)
      setResponse(res)

      if (res.ok) {
        setStatus('success')
        setErrorMessage(null)
      } else if (res.timedOut) {
        setStatus('timeout')
        setErrorMessage(res.errorMessage)
      } else {
        setStatus('error')
        setErrorMessage(res.errorMessage)
      }

      return res
    },
    [],
  )

  const reset = useCallback(() => {
    setStatus('idle')
    setErrorMessage(null)
    setResponse(null)
  }, [])

  return {
    status,
    errorMessage,
    response,
    isLoading: status === 'loading',
    isSuccess: status === 'success',
    isError: status === 'error',
    isTimeout: status === 'timeout',
    submit,
    reset,
  }
}

export default useForm
