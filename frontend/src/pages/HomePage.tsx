import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import HeroSection from '../components/HeroSection'
import styles from './HomePage.module.css'

// ---------------------------------------------------------------------------
// HomePage
// ---------------------------------------------------------------------------

/**
 * A single service area summary returned by `GET /api/profile/services`.
 * Mirrors the `ServiceBlock` model (serialised as camelCase JSON).
 */
interface ServiceBlock {
  id: string
  category: string
  title: string
  description: string
  offerings: string[]
}

/** Heading shown above the service area summaries. */
const SERVICES_HEADING = 'What We Do'

/**
 * Maximum number of service areas the home page summarises. The design limits
 * the home page to 2â€“6 areas (Requirement 2.2); extras are truncated here and
 * fewer are rendered as-is so the section is never empty.
 */
const MAX_SERVICE_AREAS = 6

/**
 * localStorage key under which the last successful services response is cached
 * so the summaries remain populated when the API is unavailable (Requirement 3.8).
 */
const SERVICES_CACHE_KEY = 'nnm:services'

/** Read the cached services list from localStorage, tolerating absent/corrupt data. */
function readCachedServices(): ServiceBlock[] | null {
  try {
    const raw = localStorage.getItem(SERVICES_CACHE_KEY)
    if (!raw) {
      return null
    }
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) {
      return null
    }
    return parsed as ServiceBlock[]
  } catch {
    return null
  }
}

/** Persist the latest successful services list to localStorage (Requirement 3.8). */
function writeCachedServices(services: ServiceBlock[]): void {
  try {
    localStorage.setItem(SERVICES_CACHE_KEY, JSON.stringify(services))
  } catch {
    // Storage may be unavailable (private mode / quota); caching is best-effort.
  }
}

/**
 * Clamp a services list to the 2â€“6 summaries the home page displays
 * (Requirement 2.2). Fewer than the minimum are still rendered as-is so the
 * section is never empty; more than the maximum are truncated.
 */
function clampServices(services: ServiceBlock[]): ServiceBlock[] {
  return services.slice(0, MAX_SERVICE_AREAS)
}

/**
 * HomePage renders the `/` route (Requirements 2.2, 3.8):
 *
 * - The {@link HeroSection} at the top.
 * - A grid of 2â€“6 service area summaries (title + short description) fetched
 *   from `GET /api/profile/services`.
 *
 * On a successful fetch the response is cached in localStorage. When the API is
 * unavailable the component renders from that cache, so the summaries are never
 * empty once a successful response has been seen (Requirement 3.8).
 */
export default function HomePage() {
  // Seed from cache so the section is populated on first paint when offline.
  const [services, setServices] = useState<ServiceBlock[]>(
    () => clampServices(readCachedServices() ?? []),
  )
  const [fromCache, setFromCache] = useState<boolean>(false)

  useEffect(() => {
    let cancelled = false

    async function loadServices() {
      const res = await apiGet<ServiceBlock[]>('/api/profile/services')
      if (cancelled) {
        return
      }

      if (res.ok && Array.isArray(res.data)) {
        // Fresh data: render it and refresh the cache.
        writeCachedServices(res.data)
        setServices(clampServices(res.data))
        setFromCache(false)
        return
      }

      // API unavailable: fall back to the last successful response (Requirement 3.8).
      const cached = readCachedServices()
      if (cached && cached.length > 0) {
        setServices(clampServices(cached))
        setFromCache(true)
      }
    }

    void loadServices()

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main>
      <HeroSection />

      <section className={styles.mission} aria-label="Our mission">
        <div className={styles.missionInner}>
          <p className={styles.missionEyebrow}>Our Mission</p>
          <h2 className={styles.missionHeading}>
            Helping businesses run smoothly and grow with confidence.
          </h2>
          <p className={styles.missionBody}>
            NNM Advisory brings together accounting expertise and practical
            technology to give small and growing businesses clarity over their
            finances and the systems that run them. We pair dependable
            bookkeeping, tax, and reporting with modern IT so our clients can
            make confident decisions and focus on what they do best.
          </p>
          <div className={styles.missionStats}>
            <div className={styles.stat}>
              <span className={styles.statValue}>2</span>
              <span className={styles.statLabel}>Core practice areas</span>
            </div>
            <div className={styles.stat}>
              <span className={styles.statValue}>Qualified</span>
              <span className={styles.statLabel}>Chartered-accounting led</span>
            </div>
            <div className={styles.stat}>
              <span className={styles.statValue}>End&#8209;to&#8209;end</span>
              <span className={styles.statLabel}>Accounting &amp; IT</span>
            </div>
          </div>
        </div>
      </section>

      {services.length > 0 && (
        <section className={styles.services} aria-label="Service areas">
          <div className={styles.container}>
            <h2 className={styles.heading}>{SERVICES_HEADING}</h2>
            <p className={styles.intro}>
              Practical accounting and technology services, plus survey tools, to
              help your business run smoothly and make confident decisions.
            </p>

            {fromCache && (
              <p className={styles.status} role="status">
                Showing recently saved information.
              </p>
            )}

            <div className={styles.grid}>
              {services.map((service) => (
                <article key={service.id} className={styles.card}>
                  <h3 className={styles.cardTitle}>{service.title}</h3>
                  <p className={styles.cardDescription}>{service.description}</p>
                </article>
              ))}
            </div>
          </div>
        </section>
      )}
    </main>
  )
}
