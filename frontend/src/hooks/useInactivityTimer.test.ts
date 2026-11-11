/**
 * useInactivityTimer.test.ts
 *
 * Verifies the admin inactivity timeout (Requirement 8.6):
 *  - onTimeout fires after 60 minutes of inactivity while enabled
 *  - API activity resets the countdown
 *  - the timer never fires while disabled (no active admin session)
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useInactivityTimer, SIXTY_MINUTES_MS } from './useInactivityTimer'
import { notifyActivity } from '../activityNotifier'

describe('useInactivityTimer', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
  })

  it('calls onTimeout after 60 minutes of inactivity when enabled', () => {
    const onTimeout = vi.fn()
    renderHook(() => useInactivityTimer({ enabled: true, onTimeout }))

    // Just before the window elapses, nothing should have fired.
    vi.advanceTimersByTime(SIXTY_MINUTES_MS - 1)
    expect(onTimeout).not.toHaveBeenCalled()

    // Crossing the 60-minute boundary fires the timeout.
    vi.advanceTimersByTime(1)
    expect(onTimeout).toHaveBeenCalledTimes(1)
  })

  it('resets the countdown on API activity', () => {
    const onTimeout = vi.fn()
    renderHook(() => useInactivityTimer({ enabled: true, onTimeout }))

    // Advance almost to the limit, then register activity to reset the timer.
    vi.advanceTimersByTime(SIXTY_MINUTES_MS - 1000)
    notifyActivity()

    // The original window would have elapsed by now, but activity reset it.
    vi.advanceTimersByTime(2000)
    expect(onTimeout).not.toHaveBeenCalled()

    // A further full window with no activity does fire the timeout.
    vi.advanceTimersByTime(SIXTY_MINUTES_MS)
    expect(onTimeout).toHaveBeenCalledTimes(1)
  })

  it('does not fire while disabled', () => {
    const onTimeout = vi.fn()
    renderHook(() => useInactivityTimer({ enabled: false, onTimeout }))

    vi.advanceTimersByTime(SIXTY_MINUTES_MS * 2)
    expect(onTimeout).not.toHaveBeenCalled()
  })

  it('stops firing once it becomes disabled', () => {
    const onTimeout = vi.fn()
    const { rerender } = renderHook(
      ({ enabled }: { enabled: boolean }) =>
        useInactivityTimer({ enabled, onTimeout }),
      { initialProps: { enabled: true } },
    )

    // Disable (e.g. token cleared) before the window elapses.
    vi.advanceTimersByTime(SIXTY_MINUTES_MS - 1000)
    rerender({ enabled: false })

    vi.advanceTimersByTime(SIXTY_MINUTES_MS * 2)
    expect(onTimeout).not.toHaveBeenCalled()
  })
})
