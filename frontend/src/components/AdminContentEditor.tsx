import { useEffect, useRef, useState, type FormEvent } from 'react'
import { apiPut } from '../apiClient'
import styles from './AdminContentEditor.module.css'

// ---------------------------------------------------------------------------
// AdminContentEditor (Requirements 9.1, 9.6, 9.7, 9.8)
//
// A config-driven editor for the three kinds of editable content surfaced on
// the admin dashboard:
//   - tagline     → single text field, ≤ 150 chars, PUT /api/admin/profile
//   - service     → title ≤ 100, description ≤ 500, offerings (one per line),
//                   PUT /api/admin/services/{id}
//   - leadership  → biography textarea ≤ 2000, PUT /api/admin/leadership/{id}
//
// Behaviour:
//   - Live preview panel reflects the current input, updated within ~1 second
//     of the admin ceasing input via a debounce (Requirement 9.6).
//   - Each field shows a character counter that turns red when the field
//     exceeds its limit; the Save button is disabled while any field is over
//     its limit or the content is empty/whitespace-only (Requirement 9.6).
//   - On Save: PUT to the correct admin endpoint. On success (2xx) a
//     confirmation message is shown and kept visible for at least 3 seconds
//     (Requirement 9.7). On failure (non-2xx) an error message is shown and the
//     displayed stored content is left unchanged (Requirement 9.8).
//
// The component is reusable: callers pass an `EditTarget` descriptor plus
// initial values and an `onSaved` callback used to refresh the parent view.
// ---------------------------------------------------------------------------

/** Field length limits enforced client-side (mirrors backend validation). */
export const EDITOR_LIMITS = {
  tagline: 150,
  serviceTitle: 100,
  serviceDescription: 500,
  biography: 2000,
} as const

/** How long (ms) to wait after the last keystroke before updating the preview. */
const PREVIEW_DEBOUNCE_MS = 500

/** Minimum time (ms) the success confirmation stays visible (Requirement 9.7). */
const CONFIRMATION_VISIBLE_MS = 3000

/**
 * Identifies which editable item is being edited. Mirrors the dashboard's
 * `EditTarget` shape so the dashboard can pass its selection straight through.
 */
export type EditTarget =
  | { kind: 'tagline' }
  | { kind: 'service'; id: string; title: string }
  | { kind: 'leadership'; id: string; name: string }

/** Initial values for each editor variant. */
export interface TaglineInitialValues {
  tagline: string
}

export interface ServiceInitialValues {
  title: string
  description: string
  offerings?: string[]
}

export interface LeadershipInitialValues {
  biography: string
  photoS3Key?: string
}

export interface AdminContentEditorProps {
  /** The content item being edited. */
  target: EditTarget
  /**
   * Initial field values. The shape must match `target.kind`:
   *   tagline     → TaglineInitialValues
   *   service     → ServiceInitialValues
   *   leadership  → LeadershipInitialValues
   */
  initialValues:
    | TaglineInitialValues
    | ServiceInitialValues
    | LeadershipInitialValues
  /** Invoked after a successful save so the parent can refresh its data. */
  onSaved?: () => void
}

/** Save request lifecycle status. */
type SaveStatus = 'idle' | 'saving' | 'success' | 'error'

/** Returns a value as a string, treating undefined/null as empty. */
const asString = (v: unknown): string => (typeof v === 'string' ? v : '')

/** A labelled field configuration used to render inputs generically. */
interface FieldConfig {
  key: string
  label: string
  value: string
  limit: number
  multiline: boolean
  /** When false the save button is disabled (field is empty / over limit). */
  helpText?: string
}

/**
 * AdminContentEditor renders the appropriate form for the given edit target,
 * a live preview panel, per-field character counters, and save handling.
 */
export default function AdminContentEditor({
  target,
  initialValues,
  onSaved,
}: AdminContentEditorProps) {
  // ── Field state, initialised per target kind ──────────────────────────────
  const [tagline, setTagline] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [offerings, setOfferings] = useState('')
  const [biography, setBiography] = useState('')

  // Reset all fields whenever the target or initial values change so the editor
  // reflects the currently-selected item (and discards prior edits).
  useEffect(() => {
    if (target.kind === 'tagline') {
      const iv = initialValues as TaglineInitialValues
      setTagline(asString(iv.tagline))
    } else if (target.kind === 'service') {
      const iv = initialValues as ServiceInitialValues
      setTitle(asString(iv.title))
      setDescription(asString(iv.description))
      setOfferings(Array.isArray(iv.offerings) ? iv.offerings.join('\n') : '')
    } else {
      const iv = initialValues as LeadershipInitialValues
      setBiography(asString(iv.biography))
    }
    // Reset save status when switching targets.
    setStatus('idle')
    setErrorMessage(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, initialValues])

  // ── Save lifecycle state ──────────────────────────────────────────────────
  const [status, setStatus] = useState<SaveStatus>('idle')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  // Timer that keeps the confirmation visible for the minimum duration.
  const confirmationTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(
    () => () => {
      if (confirmationTimer.current) clearTimeout(confirmationTimer.current)
    },
    [],
  )

  // ── Build the field configuration for the current target ──────────────────
  let fields: FieldConfig[]
  if (target.kind === 'tagline') {
    fields = [
      {
        key: 'tagline',
        label: 'Tagline',
        value: tagline,
        limit: EDITOR_LIMITS.tagline,
        multiline: false,
      },
    ]
  } else if (target.kind === 'service') {
    fields = [
      {
        key: 'title',
        label: 'Title',
        value: title,
        limit: EDITOR_LIMITS.serviceTitle,
        multiline: false,
      },
      {
        key: 'description',
        label: 'Description',
        value: description,
        limit: EDITOR_LIMITS.serviceDescription,
        multiline: true,
      },
    ]
  } else {
    fields = [
      {
        key: 'biography',
        label: 'Biography',
        value: biography,
        limit: EDITOR_LIMITS.biography,
        multiline: true,
      },
    ]
  }

  // ── Debounced live preview (Requirement 9.6) ──────────────────────────────
  // Build the preview snapshot from the current fields, then debounce it so the
  // preview panel settles within ~1 second of the admin ceasing input.
  const [previewFields, setPreviewFields] = useState<FieldConfig[]>(fields)
  // Key the preview on the concatenated field values so edits trigger it.
  const previewKey = fields.map((f) => f.value).join('\u0000')
  useEffect(() => {
    const handle = setTimeout(() => setPreviewFields(fields), PREVIEW_DEBOUNCE_MS)
    return () => clearTimeout(handle)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewKey])

  // ── Validity: every field within limit and no whitespace-only content ─────
  const anyOverLimit = fields.some((f) => f.value.length > f.limit)
  const anyEmpty = fields.some((f) => f.value.trim().length === 0)
  const canSave = !anyOverLimit && !anyEmpty && status !== 'saving'

  // ── Change handler per field key ──────────────────────────────────────────
  function handleFieldChange(key: string, value: string) {
    switch (key) {
      case 'tagline':
        setTagline(value)
        break
      case 'title':
        setTitle(value)
        break
      case 'description':
        setDescription(value)
        break
      case 'biography':
        setBiography(value)
        break
    }
    // A new edit clears a prior error so the admin isn't left with stale state.
    if (status === 'error') {
      setStatus('idle')
      setErrorMessage(null)
    }
  }

  // ── Save (Requirements 9.7, 9.8) ──────────────────────────────────────────
  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canSave) return

    setStatus('saving')
    setErrorMessage(null)

    let path: string
    let body: Record<string, unknown>
    if (target.kind === 'tagline') {
      path = '/api/admin/profile'
      body = { tagline: tagline.trim() }
    } else if (target.kind === 'service') {
      path = `/api/admin/services/${target.id}`
      body = {
        title: title.trim(),
        description: description.trim(),
        offerings: offerings
          .split('\n')
          .map((line) => line.trim())
          .filter((line) => line.length > 0),
      }
    } else {
      path = `/api/admin/leadership/${target.id}`
      body = { biography: biography.trim() }
      const photoKey = (initialValues as LeadershipInitialValues).photoS3Key
      if (photoKey) body.photoS3Key = photoKey
    }

    const res = await apiPut(path, body)

    if (res.ok) {
      // Requirement 9.7: show confirmation and keep it visible ≥ 3 seconds.
      setStatus('success')
      if (confirmationTimer.current) clearTimeout(confirmationTimer.current)
      confirmationTimer.current = setTimeout(() => {
        setStatus('idle')
      }, CONFIRMATION_VISIBLE_MS)
      onSaved?.()
    } else {
      // Requirement 9.8: show error; stored content is left unchanged because
      // we do not mutate the displayed values on failure.
      setStatus('error')
      setErrorMessage(
        res.errorMessage ??
          'The save failed. Please try again. Your changes have not been saved.',
      )
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <form className={styles.editor} onSubmit={handleSubmit} noValidate>
      {status === 'success' && (
        <div className={styles.successBanner} role="status" aria-live="polite">
          Your changes have been saved.
        </div>
      )}
      {status === 'error' && errorMessage && (
        <div className={styles.errorBanner} role="alert">
          {errorMessage}
        </div>
      )}

      <div className={styles.fields}>
        {fields.map((field) => {
          const over = field.value.length > field.limit
          const counterId = `editor-${field.key}-counter`
          const inputId = `editor-${field.key}`
          return (
            <div key={field.key} className={styles.field}>
              <label className={styles.label} htmlFor={inputId}>
                {field.label}
              </label>
              {field.multiline ? (
                <textarea
                  id={inputId}
                  className={styles.textarea}
                  value={field.value}
                  rows={field.key === 'biography' ? 8 : 4}
                  onChange={(e) => handleFieldChange(field.key, e.target.value)}
                  disabled={status === 'saving'}
                  aria-invalid={over || undefined}
                  aria-describedby={counterId}
                />
              ) : (
                <input
                  id={inputId}
                  type="text"
                  className={styles.input}
                  value={field.value}
                  onChange={(e) => handleFieldChange(field.key, e.target.value)}
                  disabled={status === 'saving'}
                  aria-invalid={over || undefined}
                  aria-describedby={counterId}
                />
              )}
              <span
                id={counterId}
                className={over ? styles.counterOver : styles.counter}
                aria-live="polite"
              >
                {field.value.length} / {field.limit}
                {over && ' — over the limit'}
              </span>
            </div>
          )
        })}

        {target.kind === 'service' && (
          <div className={styles.field}>
            <label className={styles.label} htmlFor="editor-offerings">
              Offerings (one per line)
            </label>
            <textarea
              id="editor-offerings"
              className={styles.textarea}
              value={offerings}
              rows={4}
              onChange={(e) => setOfferings(e.target.value)}
              disabled={status === 'saving'}
            />
          </div>
        )}
      </div>

      {/* Live preview panel (Requirement 9.6). */}
      <section className={styles.preview} aria-label="Live preview">
        <h3 className={styles.previewHeading}>Live preview</h3>
        {previewFields.map((field) => (
          <div key={field.key} className={styles.previewItem}>
            <span className={styles.previewLabel}>{field.label}</span>
            <p className={styles.previewValue}>
              {field.value.trim().length > 0 ? (
                field.value
              ) : (
                <em className={styles.previewEmpty}>Nothing to preview yet.</em>
              )}
            </p>
          </div>
        ))}
      </section>

      <button type="submit" className={styles.save} disabled={!canSave}>
        {status === 'saving' ? 'Saving…' : 'Save changes'}
      </button>
    </form>
  )
}
