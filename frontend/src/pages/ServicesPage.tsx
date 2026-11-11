import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import ServiceBlock, { type ServiceBlockData } from '../components/ServiceBlock'
import styles from './ServicesPage.module.css'

// ---------------------------------------------------------------------------
// ServicesPage
// ---------------------------------------------------------------------------

/** Page heading and introductory copy. */
const PAGE_HEADING = 'Our Services'
const PAGE_INTRO =
  'NNM Advisory brings together accounting, technology, and insight-gathering ' +
  'services to help your business run smoothly and grow with confidence.'

/**
 * localStorage key under which the last successful services response is cached
 * so the blocks remain populated when the API is unavailable (Requirement 3.8).
 * Shared with HomePage so a successful load on either page warms the cache.
 */
const SERVICES_CACHE_KEY = 'nnm:services'

/** Maps a service category to its in-app destination and link label. */
const SERVICE_LINKS: Record<string, { href: string; label: string }> = {
  accounting: { href: '/services/accounting', label: 'Explore accounting' },
  it: { href: '/services/it', label: 'Explore IT services' },
  customer_survey: { href: '/surveys/customer', label: 'Take the customer survey' },
  employee_survey: { href: '/surveys/employee', label: 'Take the employee survey' },
}

/**
 * Static fallback set of the four Service Blocks (Requirements 3.1–3.5).
 *
 * Rendered only when the API is unavailable AND no cached response exists, so
 * the page never shows empty blocks (Requirement 3.8). Once a successful API
 * response has been seen it replaces this copy and is cached for later offline
 * renders.
 */
const FALLBACK_SERVICES: ServiceBlockData[] = [
  {
    id: 'fallback-accounting',
    category: 'accounting',
    title: 'Accounting Services',
    description:
      'End-to-end accounting support that keeps your finances accurate, ' +
      'compliant, and ready for decision-making.',
    offerings: ['Bookkeeping', 'Tax services', 'Financial analysis and insights'],
  },
  {
    id: 'fallback-it',
    category: 'it',
    title: 'IT Services',
    description:
      'Practical technology services that set up and support the systems your ' +
      'business relies on every day.',
    offerings: [
      'Financial system setup for payroll and HR',
      'Website development',
      'IT infrastructure',
    ],
  },
  {
    id: 'fallback-customer-survey',
    category: 'customer_survey',
    title: 'Customer Surveys',
    description:
      'Structured customer surveys that capture satisfaction and loyalty so ' +
      'you can act on what your customers value most.',
    offerings: [],
  },
  {
    id: 'fallback-employee-survey',
    category: 'employee_survey',
    title: 'Employee Surveys',
    description:
      'Anonymous employee surveys that surface engagement and workplace ' +
      'sentiment to help you build a stronger team.',
    offerings: [],
  },
]

/** Read the cached services list from localStorage, tolerating absent/corrupt data. */
function readCachedServices(): ServiceBlockData[] | null {
  try {
    const raw = localStorage.getItem(SERVICES_CACHE_KEY)
    if (!raw) {
      return null
    }
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) {
      return null
    }
    return parsed as ServiceBlockData[]
  } catch {
    return null
  }
}

/** Persist the latest successful services list to localStorage (Requirement 3.8). */
function writeCachedServices(services: ServiceBlockData[]): void {
  try {
    localStorage.setItem(SERVICES_CACHE_KEY, JSON.stringify(services))
  } catch {
    // Storage may be unavailable (private mode / quota); caching is best-effort.
  }
}

/**
 * ServicesPage renders the `/services` route (Requirements 3.1–3.5, 3.8).
 *
 * It fetches every Service Block from `GET /api/profile/services` and renders a
 * {@link ServiceBlock} for each — the four categories (accounting, it,
 * customer_survey, employee_survey), each with its title, description, and
 * offerings list where present.
 *
 * Caching/fallback (Requirement 3.8): a successful response is cached in
 * localStorage. When the API is unavailable the page renders from that cache;
 * if no cache exists it renders a static fallback of the four blocks, so the
 * blocks are never empty.
 */
export default function ServicesPage() {
  // Seed from cache (or the static fallback) so blocks are populated on first
  // paint even before the API responds, and never empty when it fails.
  const [services, setServices] = useState<ServiceBlockData[]>(
    () => readCachedServices() ?? FALLBACK_SERVICES,
  )
  const [fromCache, setFromCache] = useState<boolean>(false)

  useEffect(() => {
    let cancelled = false

    async function loadServices() {
      const res = await apiGet<ServiceBlockData[]>('/api/profile/services')
      if (cancelled) {
        return
      }

      if (res.ok && Array.isArray(res.data) && res.data.length > 0) {
        // Fresh data: render it and refresh the cache.
        writeCachedServices(res.data)
        setServices(res.data)
        setFromCache(false)
        return
      }

      // API unavailable: fall back to the last successful response, or the
      // static fallback set if nothing has ever been cached (Requirement 3.8).
      const cached = readCachedServices()
      if (cached && cached.length > 0) {
        setServices(cached)
        setFromCache(true)
      } else {
        setServices(FALLBACK_SERVICES)
        setFromCache(true)
      }
    }

    void loadServices()

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>
        <p className={styles.intro}>{PAGE_INTRO}</p>

        {fromCache && (
          <p className={styles.status} role="status">
            Showing recently saved information.
          </p>
        )}

        <section className={styles.grid} aria-label="Service blocks">
          {services.map((block) => {
            const link = SERVICE_LINKS[block.category]
            return (
              <ServiceBlock
                key={block.id}
                block={block}
                href={link?.href}
                linkLabel={link?.label}
              />
            )
          })}
        </section>
      </div>
    </main>
  )
}
