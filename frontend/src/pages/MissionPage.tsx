import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import styles from './MissionPage.module.css'

// ---------------------------------------------------------------------------
// MissionPage - /mission
// Company mission content is read from the database via GET /api/profile.
// ---------------------------------------------------------------------------

interface CompanyContent {
  contentKey: string
  contentValue: string
}

const CACHE_KEY = 'nnm:profile'

/** Fallback copy used only if the API and cache are both unavailable. */
const FALLBACK: Record<string, string> = {
  mission_heading: 'Helping businesses run smoothly and grow with confidence.',
  mission_statement:
    'Our mission is to give small and growing businesses the financial clarity and modern technology they need to make confident decisions.',
  mission_body:
    'NNM Advisory brings together qualified chartered-accounting expertise and hands-on technology under one roof.',
}

function readCache(): CompanyContent[] | null {
  try {
    const raw = localStorage.getItem(CACHE_KEY)
    const parsed = raw ? (JSON.parse(raw) as unknown) : null
    return Array.isArray(parsed) ? (parsed as CompanyContent[]) : null
  } catch {
    return null
  }
}

export default function MissionPage() {
  const [content, setContent] = useState<Record<string, string>>(() => {
    const cached = readCache()
    if (cached) return Object.fromEntries(cached.map((c) => [c.contentKey, c.contentValue]))
    return FALLBACK
  })

  useEffect(() => {
    let cancelled = false
    async function load() {
      const res = await apiGet<CompanyContent[]>('/api/profile')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data)) {
        try {
          localStorage.setItem(CACHE_KEY, JSON.stringify(res.data))
        } catch {
          // best-effort cache
        }
        setContent(Object.fromEntries(res.data.map((c) => [c.contentKey, c.contentValue])))
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [])

  const get = (k: string) => content[k] ?? FALLBACK[k] ?? ''

  return (
    <main>
      <header className={styles.hero}>
        <div className={styles.heroInner}>
          <p className={styles.eyebrow}>Our Mission</p>
          <h1 className={styles.title}>{get('mission_heading')}</h1>
          <p className={styles.lede}>{get('mission_statement')}</p>
        </div>
      </header>

      <section className={styles.body} aria-label="Mission statement">
        <div className={styles.container}>
          <h2 className={styles.sectionHeading}>What drives us</h2>
          <p className={styles.bodyText}>{get('mission_body')}</p>

          <div className={styles.values}>
            <div className={styles.value}>
              <h3 className={styles.valueTitle}>Clarity</h3>
              <p className={styles.valueBody}>Plain-language financial information you can act on with confidence.</p>
            </div>
            <div className={styles.value}>
              <h3 className={styles.valueTitle}>Dependability</h3>
              <p className={styles.valueBody}>Accurate, on-time work backed by qualified chartered-accounting expertise.</p>
            </div>
            <div className={styles.value}>
              <h3 className={styles.valueTitle}>Modern technology</h3>
              <p className={styles.valueBody}>Practical cloud and AI-enabled solutions, right-sized for your business.</p>
            </div>
          </div>
        </div>
      </section>

      <section className={styles.ctaBand} aria-label="Get in touch">
        <div className={styles.container}>
          <h2 className={styles.ctaHeading}>Ready to work with us?</h2>
          <a className={styles.ctaLink} href="/contact">Contact us &#8594;</a>
        </div>
      </section>
    </main>
  )
}
