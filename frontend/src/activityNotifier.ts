/**
 * activityNotifier.ts
 *
 * A tiny module-level publish/subscribe channel used to signal "API activity"
 * to interested listeners (Requirement 8.6 — admin inactivity timeout).
 *
 * The `apiClient` calls {@link notifyActivity} whenever it performs a request,
 * and the admin inactivity timer (wired up in `AppContext`) subscribes via
 * {@link subscribeToActivity} to reset its 60-minute countdown on every call.
 *
 * Keeping this as a standalone module avoids coupling `apiClient` to React or
 * to `AppContext`: `apiClient` only ever emits an activity event, and whoever
 * cares can listen. This keeps the change to `apiClient` purely additive.
 */

type ActivityListener = () => void

const listeners = new Set<ActivityListener>()

/**
 * Notify all subscribers that API activity has just occurred.
 * Safe to call from anywhere; listeners are invoked synchronously.
 */
export function notifyActivity(): void {
  // Copy to an array first so a listener that unsubscribes during iteration
  // cannot disturb the Set being iterated.
  for (const listener of Array.from(listeners)) {
    try {
      listener()
    } catch {
      // A misbehaving listener must not break activity notification for others
      // or for the caller (apiClient) that triggered it.
    }
  }
}

/**
 * Subscribe to API activity events.
 *
 * @param listener Callback invoked on every {@link notifyActivity} call.
 * @returns An unsubscribe function that removes the listener.
 */
export function subscribeToActivity(listener: ActivityListener): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}
