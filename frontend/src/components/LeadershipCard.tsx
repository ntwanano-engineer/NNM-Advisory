import { useEffect, useState } from 'react'
import styles from './LeadershipCard.module.css'

// ---------------------------------------------------------------------------
// LeadershipCard — Requirement 4.4
//
// Renders a single Leadership Profile: a profile photograph, full name, title,
// and biography. The photo is served via a pre-signed URL supplied by the API
// (`GET /api/profile/leadership`) which may be null/expired. The photograph is
// constrained by CSS to a minimum of 200×200px and a maximum of 800×800px.
//
// When no photo URL is supplied, or when the <img> fails to load (e.g. an
// expired pre-signed URL), the card shows an initials-based fallback avatar so
// it never displays a broken image.
// ---------------------------------------------------------------------------

/**
 * A single leadership profile as returned by `GET /api/profile/leadership`.
 * Mirrors the `LeadershipProfile` model. `photoUrl` is a pre-signed URL that
 * may be null when no photo is stored (or unavailable).
 */
export interface LeadershipProfile {
  /** Full name of the leader (≤ 100 characters). */
  fullName: string
  /** Job title (≤ 100 characters). */
  title: string
  /** Biography text (≤ 2000 characters). */
  biography: string
  /** Pre-signed photo URL supplied by the API; may be null/absent. */
  photoUrl?: string | null
}

export interface LeadershipCardProps {
  /** The leadership profile to display. */
  profile: LeadershipProfile
}

/**
 * Derive up-to-two uppercase initials from a full name for the fallback avatar.
 * Falls back to "?" when no usable name parts are present.
 */
function getInitials(fullName: string): string {
  const parts = fullName
    .trim()
    .split(/\s+/)
    .filter((p) => p.length > 0)

  if (parts.length === 0) {
    return '?'
  }

  const first = parts[0].charAt(0)
  const last = parts.length > 1 ? parts[parts.length - 1].charAt(0) : ''

  return (first + last).toUpperCase()
}

/**
 * LeadershipCard displays one Leadership Profile (Requirement 4.4):
 *
 * - A profile photograph rendered from the pre-signed `photoUrl`, constrained
 *   by the CSS module to min 200×200px / max 800×800px with `object-fit: cover`.
 * - The leader's full name, title, and biography.
 * - An initials-based fallback avatar when `photoUrl` is missing or the image
 *   fails to load, so a broken image is never shown.
 */
export default function LeadershipCard({ profile }: LeadershipCardProps) {
  const { fullName, title, biography, photoUrl } = profile

  // Track whether the image failed to load so we can swap to the fallback.
  const [imageFailed, setImageFailed] = useState<boolean>(false)

  // Reset the failed state if the profile's photo URL changes (e.g. a refetch
  // yields a fresh pre-signed URL for the same card).
  useEffect(() => {
    setImageFailed(false)
  }, [photoUrl])

  const showFallback = !photoUrl || imageFailed

  return (
    <article className={styles.card}>
      <div className={styles.photoWrapper}>
        {showFallback ? (
          <div
            className={styles.photoFallback}
            role="img"
            aria-label={`${fullName} photo placeholder`}
          >
            <span className={styles.initials}>{getInitials(fullName)}</span>
          </div>
        ) : (
          <img
            className={styles.photo}
            src={photoUrl}
            alt={`${fullName}, ${title}`}
            onError={() => setImageFailed(true)}
          />
        )}
      </div>

      <h3 className={styles.name}>{fullName}</h3>
      <p className={styles.title}>{title}</p>
      <p className={styles.biography}>{biography}</p>
    </article>
  )
}
