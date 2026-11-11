/**
 * useInactivityTimer.ts
 *
 * Drives the admin session inactivity timeout (Requirement 8.6).
 *
 * While `enabled` is true (i.e. an authenticated admin session is active), the
 * hook subscribes to API activity events emitted by `apiClient` via the
 * {@link subscribeToActivity} channel. Every activity event resets a countdown
 * of `timeoutMs` (default 60 minutes). If the countdown elapses with no
 * activity, `onTimeout` is invoked — the caller uses this to clear the JWT and
 * redirect the admin to the login page.
 *
 * When `enabled` becomes false (token cleared/absent), the hook tears down its
 * subscription and clears any pending timer, so the timeout never fires for an
 * unauthenticated session.
 */

import { useEffect, useRef } from 'react'
import { subscribeToActivity } from '../activityNotifier'

/** 60 minutes expressed in milliseconds. */
export const SIXTY_MINUTES_MS = 60 * 60 * 1000

export interface UseInactivityTimerOptions {
  /** Whether the timer should be running (true while an admin is logged in). */
  enabled: boolean
  /** Invoked when `timeoutMs` elapses with no API activity. */
  onTimeout: () => void
  /** Inactivity window in milliseconds. Defaults to 60 minutes. */
  timeoutMs?: number
}

/**
 * Starts (and resets on activity) an inactivity countdown while `enabled`.
 */
export function useInactivityTimer({
  enabled,
  onTimeout,
  timeoutMs = SIXTY_MINUTES_MS,
}: UseInactivityTimerOptions): void {
  // Hold the latest onTimeout in a ref so resetting the timer does not require
  // re-subscribing every time the callback identity changes.
  const onTimeoutRef = useRef(onTimeout)
  useEffect(() => {
    onTimeoutRef.current = onTimeout
  }, [onTimeout])

  useEffect(() => {
    if (!enabled) return

    let timerId: ReturnType<typeof setTimeout> | undefined

    const resetTimer = () => {
      if (timerId !== undefined) {
        clearTimeout(timerId)
      }
      timerId = setTimeout(() => {
        onTimeoutRef.current()
      }, timeoutMs)
    }

    // Start the countdown immediately on enable, then reset on each activity.
    resetTimer()
    const unsubscribe = subscribeToActivity(resetTimer)

    return () => {
      unsubscribe()
      if (timerId !== undefined) {
        clearTimeout(timerId)
      }
    }
  }, [enabled, timeoutMs])
}

export default useInactivityTimer
