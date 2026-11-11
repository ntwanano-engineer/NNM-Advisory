import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import styles from './ServiceDetailPage.module.css'

// ---------------------------------------------------------------------------
// ITServicesPage - /services/it
// Offerings are static; the AI & cloud section text is read from the database
// (company_content keys it_ai_cloud_heading / it_ai_cloud_body) via /api/profile.
// ---------------------------------------------------------------------------

interface Offering {
  title: string
  description: string
}

interface CompanyContent {
  contentKey: string
  contentValue: string
}

const PROFILE_CACHE_KEY = 'nnm:profile'

const AI_CLOUD_FALLBACK: Record<string, string> = {
  it_ai_cloud_heading: 'How we implement solutions with AI and cloud technology',
  it_ai_cloud_body:
    'We design and deliver solutions cloud-first on AWS and Azure, using infrastructure-as-code so environments are repeatable, secure, and cost-aware. We automate CI/CD pipelines for reliable releases, and apply AI pragmatically to remove manual effort and surface insight from your data.',
}

const OFFERINGS: Offering[] = [
  {
    title: 'Financial System Setup for Payroll & HR',
    description:
      'Implementation and configuration of payroll and HR systems so your people are paid accurately and on time, with the right records kept automatically.',
  },
  {
    title: 'Website Development',
    description:
      'Modern, responsive business websites - from a simple presence to content-managed sites - built to represent your brand and work on every device.',
  },
  {
    title: 'IT Infrastructure',
    description:
      'Practical setup and support of the systems your business relies on: accounts, email, file storage, backups, and security fundamentals.',
  },
]

function readProfileCache(): CompanyContent[] | null {
  try {
    const raw = localStorage.getItem(PROFILE_CACHE_KEY)
    const parsed = raw ? (JSON.parse(raw) as unknown) : null
    return Array.isArray(parsed) ? (parsed as CompanyContent[]) : null
  } catch {
    return null
  }
}

export default function ITServicesPage() {
  const [content, setContent] = useState<Record<string, string>>(() => {
    const cached = readProfileCache()
    if (cached) return Object.fromEntries(cached.map((c) => [c.contentKey, c.contentValue]))
    return AI_CLOUD_FALLBACK
  })

  useEffect(() => {
    let cancelled = false
    async function load() {
      const res = await apiGet<CompanyContent[]>('/api/profile')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data)) {
        try {
          localStorage.setItem(PROFILE_CACHE_KEY, JSON.stringify(res.data))
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

  const get = (k: string) => content[k] ?? AI_CLOUD_FALLBACK[k] ?? ''

  return (
    <main>
      <header className={`${styles.hero} ${styles.heroIt}`}>
        <div className={styles.heroInner}>
          <p className={styles.eyebrow}>IT Services</p>
          <h1 className={styles.title}>Technology that quietly just works.</h1>
          <p className={styles.lede}>
            We set up and support the systems that run your business - payroll,
            websites, and infrastructure - so technology becomes an advantage, not a headache.
          </p>
        </div>
      </header>

      <section className={styles.section} aria-label="IT offerings">
        <div className={styles.container}>
          <h2 className={styles.sectionHeading}>What we offer</h2>
          <div className={styles.grid}>
            {OFFERINGS.map((o) => (
              <article key={o.title} className={styles.card}>
                <h3 className={styles.cardTitle}>{o.title}</h3>
                <p className={styles.cardBody}>{o.description}</p>
              </article>
            ))}
          </div>

          <h2 className={styles.sectionHeading} style={{ marginTop: '3.5rem' }}>
            {get('it_ai_cloud_heading')}
          </h2>
          <p className={styles.cardBody} style={{ maxWidth: '68ch', fontSize: '1.1rem' }}>
            {get('it_ai_cloud_body')}
          </p>

          <div className={styles.cta}>
            <p className={styles.ctaText}>
              Want technology that supports your growth? Tell us about your setup.
            </p>
            <a className={styles.ctaLink} href="/contact">Contact us &#8594;</a>
          </div>
        </div>
      </section>
    </main>
  )
}