import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import styles from './AdminDashboardPage.module.css'

// ---------------------------------------------------------------------------
// AdminDashboardPage — Requirement 9.1
//
// The admin dashboard overview. It is reached via the ProtectedRoute-wrapped
// `/admin` route configured in App.tsx (Requirements 8.1, 8.2), so this
// component is only the dashboard body and performs no routing or auth guarding
// of its own.
//
// The dashboard lists the three editable content sections (Requirement 9.1):
//   1. Tagline            — the hero tagline stored in company_content.
//   2. Service blocks      — one row per service, showing its title.
//   3. Leadership profiles — one row per leader, showing name and title.
//
// Each listed item exposes an "Edit" affordance. The actual editing UI is
// delivered by later tasks (20.2 AdminContentEditor, 20.3 ImageUploadField);
// until those exist, the affordances select a section/item so the editor can be
// slotted into the clearly-structured edit region below without restructuring.
// Reads use the public GET endpoints already consumed by the public pages.
// ---------------------------------------------------------------------------

/** Page-level copy. */
const PAGE_HEADING = 'Admin Dashboard'
const PAGE_INTRO =
  'Review and edit the website content. Choose a section below to update the ' +
  'tagline, a service block, or a leadership profile.'

/** Content key under which the tagline is stored in company_content. */
const TAGLINE_KEY = 'tagline'

/**
 * A single configurable content item returned by `GET /api/profile`.
 * Mirrors the `CompanyContent` model: a key/value pair.
 */
interface CompanyContent {
  contentKey: string
  contentValue: string
}

/** A service block returned by `GET /api/profile/services`. */
interface ServiceBlock {
  id: string
  category: string
  title: string
  description: string
  offerings?: string[]
  displayOrder?: number
}

/** A leadership profile returned by `GET /api/profile/leadership`. */
interface LeadershipProfile {
  id: string
  fullName: string
  title: string
  biography: string
  photoUrl?: string
  displayOrder: number
}

/**
 * Identifies which editable item the admin has selected to edit. This is the
 * hand-off point for the AdminContentEditor / ImageUploadField components added
 * in tasks 20.2 and 20.3.
 */
type EditTarget =
  | { kind: 'tagline' }
  | { kind: 'service'; id: string; title: string }
  | { kind: 'leadership'; id: string; name: string }

/** Loading/error state shared by the three independent section fetches. */
interface SectionState<T> {
  data: T | null
  loading: boolean
  error: boolean
}

const initialSection = <T,>(): SectionState<T> => ({
  data: null,
  loading: true,
  error: false,
})

/**
 * AdminDashboardPage renders the `/admin` dashboard body (Requirement 9.1):
 * a read-only overview of the three editable content sections, each row with an
 * Edit affordance that selects the target for the content editor.
 */
export default function AdminDashboardPage() {
  const [tagline, setTagline] = useState<SectionState<string>>(
    initialSection<string>(),
  )
  const [services, setServices] = useState<SectionState<ServiceBlock[]>>(
    initialSection<ServiceBlock[]>(),
  )
  const [leadership, setLeadership] = useState<
    SectionState<LeadershipProfile[]>
  >(initialSection<LeadershipProfile[]>())

  // The currently selected edit target (null when nothing is being edited).
  const [editTarget, setEditTarget] = useState<EditTarget | null>(null)

  useEffect(() => {
    let cancelled = false

    async function loadProfile() {
      const res = await apiGet<CompanyContent[]>('/api/profile')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data)) {
        const match = res.data.find((item) => item.contentKey === TAGLINE_KEY)
        setTagline({
          data: match?.contentValue ?? '',
          loading: false,
          error: false,
        })
      } else {
        setTagline({ data: null, loading: false, error: true })
      }
    }

    async function loadServices() {
      const res = await apiGet<ServiceBlock[]>('/api/profile/services')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data)) {
        setServices({ data: res.data, loading: false, error: false })
      } else {
        setServices({ data: null, loading: false, error: true })
      }
    }

    async function loadLeadership() {
      const res = await apiGet<LeadershipProfile[]>('/api/profile/leadership')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data)) {
        const ordered = [...res.data].sort(
          (a, b) => a.displayOrder - b.displayOrder,
        )
        setLeadership({ data: ordered, loading: false, error: false })
      } else {
        setLeadership({ data: null, loading: false, error: true })
      }
    }

    void loadProfile()
    void loadServices()
    void loadLeadership()

    return () => {
      cancelled = true
    }
  }, [])

  /** Human-readable label describing the current edit target for the editor region. */
  function describeEditTarget(target: EditTarget): string {
    switch (target.kind) {
      case 'tagline':
        return 'Tagline'
      case 'service':
        return `Service block: ${target.title}`
      case 'leadership':
        return `Leadership profile: ${target.name}`
    }
  }

  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>
        <p className={styles.intro}>{PAGE_INTRO}</p>

        {/* ── Tagline ─────────────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="section-tagline">
          <h2 id="section-tagline" className={styles.sectionTitle}>
            Tagline
          </h2>

          {tagline.loading && (
            <p className={styles.status} role="status">
              Loading tagline…
            </p>
          )}
          {tagline.error && (
            <p className={styles.status} role="status">
              The tagline is temporarily unavailable. Please try again later.
            </p>
          )}
          {!tagline.loading && !tagline.error && (
            <ul className={styles.list}>
              <li className={styles.row}>
                <span className={styles.itemLabel}>
                  {tagline.data ? tagline.data : <em>No tagline set.</em>}
                </span>
                <button
                  type="button"
                  className={styles.editButton}
                  onClick={() => setEditTarget({ kind: 'tagline' })}
                >
                  Edit
                </button>
              </li>
            </ul>
          )}
        </section>

        {/* ── Service blocks ──────────────────────────────────────────── */}
        <section className={styles.section} aria-labelledby="section-services">
          <h2 id="section-services" className={styles.sectionTitle}>
            Service Blocks
          </h2>

          {services.loading && (
            <p className={styles.status} role="status">
              Loading service blocks…
            </p>
          )}
          {services.error && (
            <p className={styles.status} role="status">
              Service blocks are temporarily unavailable. Please try again later.
            </p>
          )}
          {!services.loading && !services.error && services.data && (
            services.data.length > 0 ? (
              <ul className={styles.list}>
                {services.data.map((service) => (
                  <li key={service.id} className={styles.row}>
                    <span className={styles.itemLabel}>{service.title}</span>
                    <button
                      type="button"
                      className={styles.editButton}
                      onClick={() =>
                        setEditTarget({
                          kind: 'service',
                          id: service.id,
                          title: service.title,
                        })
                      }
                    >
                      Edit
                    </button>
                  </li>
                ))}
              </ul>
            ) : (
              <p className={styles.status} role="status">
                No service blocks to display.
              </p>
            )
          )}
        </section>

        {/* ── Leadership profiles ─────────────────────────────────────── */}
        <section
          className={styles.section}
          aria-labelledby="section-leadership"
        >
          <h2 id="section-leadership" className={styles.sectionTitle}>
            Leadership Profiles
          </h2>

          {leadership.loading && (
            <p className={styles.status} role="status">
              Loading leadership profiles…
            </p>
          )}
          {leadership.error && (
            <p className={styles.status} role="status">
              Leadership profiles are temporarily unavailable. Please try again
              later.
            </p>
          )}
          {!leadership.loading && !leadership.error && leadership.data && (
            leadership.data.length > 0 ? (
              <ul className={styles.list}>
                {leadership.data.map((profile) => (
                  <li key={profile.id} className={styles.row}>
                    <span className={styles.itemLabel}>
                      {profile.fullName}
                      <span className={styles.itemMeta}>{profile.title}</span>
                    </span>
                    <button
                      type="button"
                      className={styles.editButton}
                      onClick={() =>
                        setEditTarget({
                          kind: 'leadership',
                          id: profile.id,
                          name: profile.fullName,
                        })
                      }
                    >
                      Edit
                    </button>
                  </li>
                ))}
              </ul>
            ) : (
              <p className={styles.status} role="status">
                No leadership profiles to display.
              </p>
            )
          )}
        </section>

        {/* ── Edit region ─────────────────────────────────────────────────
            Clearly-structured host for the editing UI. The AdminContentEditor
            (task 20.2) and ImageUploadField (task 20.3) will render here based
            on the selected `editTarget`; until then this region confirms the
            selection and provides a way to close it. */}
        {editTarget && (
          <section
            className={styles.editor}
            aria-labelledby="section-editor"
            aria-live="polite"
          >
            <div className={styles.editorHeader}>
              <h2 id="section-editor" className={styles.sectionTitle}>
                Editing: {describeEditTarget(editTarget)}
              </h2>
              <button
                type="button"
                className={styles.editButton}
                onClick={() => setEditTarget(null)}
              >
                Close
              </button>
            </div>
            <p className={styles.status}>
              The content editor for this section is coming soon.
            </p>
          </section>
        )}
      </div>
    </main>
  )
}
