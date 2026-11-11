import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { getStoredToken } from '../apiClient'
import styles from './ImageUploadField.module.css'

// ---------------------------------------------------------------------------
// ImageUploadField (Requirements 9.3, 9.4, 9.5, 9.9)
// ---------------------------------------------------------------------------

/** Accepted MIME types for leadership profile photos (Requirement 9.3, 9.4). */
const ACCEPTED_MIME_TYPES = ['image/jpeg', 'image/png'] as const

/** Accept attribute mirroring the allowed MIME types. */
const ACCEPT_ATTR = ACCEPTED_MIME_TYPES.join(',')

/** Maximum upload size: 5 MB (Requirement 9.5). */
const MAX_FILE_BYTES = 5 * 1024 * 1024

/** Base URL from Vite env, matching apiClient's resolution strategy. */
const BASE_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? ''

/** Shared message describing the accepted formats and size cap (Requirements 9.4, 9.5). */
const ACCEPTED_FILE_MESSAGE =
  'File must be JPEG or PNG and no larger than 5 MB.'

export interface ImageUploadFieldProps {
  /** Identifier of the leadership profile the photo belongs to. */
  leadershipId: string
  /**
   * Invoked after a successful upload so the parent can refresh the
   * leadership profile display (Requirement 9.5 — new photo shown promptly).
   */
  onUploaded: () => void
}

type UploadStatus = 'idle' | 'uploading' | 'success' | 'error'

/**
 * ImageUploadField lets an admin select a JPEG/PNG profile photo, validates it
 * on the client, previews it before confirmation, and uploads it as multipart
 * form data to `POST /api/admin/leadership/{id}/photo`.
 *
 * Behaviour:
 * - On selection the file's MIME type and size are validated. An invalid file
 *   shows an error naming the accepted formats and 5 MB limit and is not kept
 *   (Requirements 9.4, 9.5).
 * - A valid selection is previewed via an object URL before the admin confirms
 *   (Requirement 9.9).
 * - Confirming POSTs the file (form field `file`) with the Bearer token. On
 *   success `onUploaded` runs so the parent can refresh; on failure an error is
 *   shown.
 */
export default function ImageUploadField({
  leadershipId,
  onUploaded,
}: ImageUploadFieldProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [status, setStatus] = useState<UploadStatus>('idle')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)

  // Revoke the active object URL when it changes or the component unmounts,
  // so the selected-file preview never leaks memory.
  useEffect(() => {
    if (!previewUrl) return
    return () => URL.revokeObjectURL(previewUrl)
  }, [previewUrl])

  function resetSelection() {
    setSelectedFile(null)
    setPreviewUrl(null)
    if (inputRef.current) {
      inputRef.current.value = ''
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setStatus('idle')
    setErrorMessage(null)

    const file = event.target.files?.[0] ?? null
    if (!file) {
      resetSelection()
      return
    }

    // Requirement 9.4 / 9.5: reject by MIME type or size before any upload.
    const typeAllowed = (ACCEPTED_MIME_TYPES as readonly string[]).includes(
      file.type,
    )
    if (!typeAllowed) {
      setStatus('error')
      setErrorMessage(
        'Unsupported file type. Accepted formats are JPEG and PNG.',
      )
      resetSelection()
      return
    }
    if (file.size > MAX_FILE_BYTES) {
      setStatus('error')
      setErrorMessage(
        'File is too large. The maximum file size is 5 MB.',
      )
      resetSelection()
      return
    }

    // Valid file: build a preview (Requirement 9.9).
    setSelectedFile(file)
    setPreviewUrl(URL.createObjectURL(file))
  }

  async function handleConfirm() {
    if (!selectedFile) return

    setStatus('uploading')
    setErrorMessage(null)

    const formData = new FormData()
    formData.append('file', selectedFile)

    const token = getStoredToken()
    const headers: Record<string, string> = {}
    if (token) {
      headers['Authorization'] = `Bearer ${token}`
    }
    // Deliberately omit Content-Type so the browser sets the multipart
    // boundary itself; apiClient's JSON client cannot do this, hence a direct
    // fetch here.

    try {
      const response = await fetch(
        `${BASE_URL}/api/admin/leadership/${encodeURIComponent(leadershipId)}/photo`,
        {
          method: 'POST',
          headers,
          body: formData,
        },
      )

      if (response.ok) {
        setStatus('success')
        resetSelection()
        onUploaded()
        return
      }

      // Prefer a server-provided reason when available.
      let serverMessage: string | null = null
      try {
        const contentType = response.headers.get('content-type') ?? ''
        if (contentType.includes('application/json')) {
          const data = (await response.json()) as { error?: string }
          serverMessage = data?.error ?? null
        }
      } catch {
        serverMessage = null
      }

      setStatus('error')
      setErrorMessage(
        serverMessage ??
          (response.status === 400
            ? ACCEPTED_FILE_MESSAGE
            : 'The photo could not be uploaded. Please try again later.'),
      )
    } catch {
      setStatus('error')
      setErrorMessage(
        'The photo could not be uploaded. Please check your connection and try again.',
      )
    }
  }

  const isUploading = status === 'uploading'

  return (
    <div className={styles.container}>
      <label className={styles.label} htmlFor={`photo-upload-${leadershipId}`}>
        Profile photograph
      </label>
      <p className={styles.hint}>{ACCEPTED_FILE_MESSAGE}</p>

      <input
        id={`photo-upload-${leadershipId}`}
        ref={inputRef}
        type="file"
        className={styles.input}
        accept={ACCEPT_ATTR}
        onChange={handleFileChange}
        disabled={isUploading}
      />

      {status === 'error' && errorMessage && (
        <p className={styles.error} role="alert">
          {errorMessage}
        </p>
      )}

      {status === 'success' && (
        <p className={styles.success} role="status" aria-live="polite">
          Photograph uploaded successfully.
        </p>
      )}

      {previewUrl && selectedFile && (
        <div className={styles.previewArea}>
          <img
            src={previewUrl}
            alt="Selected profile photograph preview"
            className={styles.preview}
          />
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.confirm}
              onClick={handleConfirm}
              disabled={isUploading}
            >
              {isUploading ? 'Uploading…' : 'Confirm upload'}
            </button>
            <button
              type="button"
              className={styles.cancel}
              onClick={resetSelection}
              disabled={isUploading}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
