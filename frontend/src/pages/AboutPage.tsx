import { useEffect, useState } from 'react'
import { apiGet } from '../apiClient'
import styles from './AboutPage.module.css'

// ---------------------------------------------------------------------------
// AboutPage - /about
// Company story + leadership (owner) profiles. Biographies are read from the
// database via GET /api/profile/leadership; photos, contact details, focus
// areas, and the education / experience timelines are presented alongside.
// ---------------------------------------------------------------------------

interface LeadershipProfile {
  id: string
  fullName: string
  title: string
  biography: string
  photoUrl: string | null
  displayOrder: number
}

/** Static presentation data keyed by the DB profile full name. */
interface OwnerExtra {
  photo: string
  email: string
  phone: string
  focus: string[]
  education: string[]
  experience: string[]
}

const OWNER_EXTRAS: Record<string, OwnerExtra> = {
  'Ntwanano Mabasa': {
    photo: '/owner-ntwanano.jpg',
    email: 'ntwani2014@gmail.com',
    phone: '083 694 8613',
    focus: ['Operations', 'Software engineering', 'Cloud & DevOps'],
    education: [
      'BSc Honours in Computer Science - University of Johannesburg (2015-2016)',
      'BSc in Mathematical Sciences - University of Johannesburg (2011-2014)',
    ],
    experience: [
      'Senior Software Engineer, Rand Merchant Bank (2025-present)',
      'Software Engineer - Old Mutual, FNB, and African Bank',
      '.NET, Python & React; AWS infrastructure-as-code (CloudFormation) and Azure CI/CD',
    ],
  },
  'Ntshovelo Priscilla Novela': {
    photo: '/owner-ntshovelo.jpg',
    email: 'ntshovelopriscilla@gmail.com',
    phone: '082 405 5665',
    focus: ['IFRS reporting', 'Financial controls', 'Audit & risk'],
    education: [
      'SAICA Assessment of Professional Competence (APC) - passed December 2024',
      'SAICA Initial Test of Competence (ITC) - passed June 2023',
      'Certificate in the Theory of Accounting (CTA) - UNISA (2022)',
      'BAccSci - University of the Witwatersrand (2018-2020), Golden Key (2020)',
    ],
    experience: [
      'Senior Accountant, Tourvest (2026-present)',
      'Audit Senior, Deloitte (2022-2026)',
      'Clients included Powerfleet, Africa Data Centres, Merafe, Tiger Brands, Motus, Yokohama, AECI, CA&S and Eskom',
    ],
  },
}

const LEADERSHIP_CACHE_KEY = 'nnm:leadership'

/** Fallback bios used only if the API and cache are both unavailable. */
const FALLBACK_PROFILES: LeadershipProfile[] = [
  {
    id: 'fallback-coo',
    fullName: 'Ntwanano Mabasa',
    title: 'Chief Operating Officer (COO)',
    biography:
      'Ntwanano Trevor Mabasa is Chief Operating Officer of NNM Advisory, leading day-to-day operations across the firm\u2019s accounting and IT service lines.',
    photoUrl: null,
    displayOrder: 1,
  },
  {
    id: 'fallback-cio',
    fullName: 'Ntshovelo Priscilla Novela',
    title: 'Chief Information Officer (CIO)',
    biography:
      'Ntshovelo Priscilla Novela is a qualified Chartered Accountant, CA(SA), and a founder of NNM Advisory, leading the firm\u2019s accounting, reporting, and financial-controls practice.',
    photoUrl: null,
    displayOrder: 2,
  },
]

function readCache(): LeadershipProfile[] | null {
  try {
    const raw = localStorage.getItem(LEADERSHIP_CACHE_KEY)
    const parsed = raw ? (JSON.parse(raw) as unknown) : null
    return Array.isArray(parsed) ? (parsed as LeadershipProfile[]) : null
  } catch {
    return null
  }
}

export default function AboutPage() {
  const [profiles, setProfiles] = useState<LeadershipProfile[]>(
    () => readCache() ?? FALLBACK_PROFILES,
  )

  useEffect(() => {
    let cancelled = false
    async function load() {
      const res = await apiGet<LeadershipProfile[]>('/api/profile/leadership')
      if (cancelled) return
      if (res.ok && Array.isArray(res.data) && res.data.length > 0) {
        try {
          localStorage.setItem(LEADERSHIP_CACHE_KEY, JSON.stringify(res.data))
        } catch {
          // best-effort cache
        }
        setProfiles([...res.data].sort((a, b) => a.displayOrder - b.displayOrder))
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main>
      <header className={styles.hero}>
        <div className={styles.heroInner}>
          <p className={styles.eyebrow}>About Us</p>
          <h1 className={styles.title}>A practice built on clarity and trust.</h1>
          <p className={styles.lede}>
            NNM Advisory is a founder-led firm pairing qualified accounting with
            practical technology, helping small and growing businesses stay
            compliant, organised, and ready to grow.
          </p>
        </div>
      </header>

      <section className={styles.story} aria-label="Our story">
        <div className={styles.container}>
          <h2 className={styles.sectionHeading}>Who we are</h2>
          <div className={styles.storyGrid}>
            <p className={styles.storyBody}>
              NNM Advisory was founded on a simple idea: that good financial
              information and well-run systems should be within reach of every
              business, not just large ones. We combine the discipline of
              chartered accounting with hands-on technology services so our
              clients get a single, trusted partner for both.
            </p>
            <p className={styles.storyBody}>
              From bookkeeping and tax to payroll systems, websites, and IT
              infrastructure, we focus on the practical work that keeps a
              business healthy - and we explain it in plain language, so you
              always understand the numbers behind your decisions.
            </p>
          </div>
        </div>
      </section>

      <section className={styles.leadership} aria-label="Leadership">
        <div className={styles.container}>
          <h2 className={styles.sectionHeading}>Meet the founders</h2>
          <div className={styles.owners}>
            {profiles.map((p) => {
              const extra = OWNER_EXTRAS[p.fullName]
              const photo = extra?.photo ?? p.photoUrl ?? '/owner-ntwanano.jpg'
              return (
                <article key={p.id} className={styles.owner}>
                  <div className={styles.photoWrap}>
                    <img className={styles.photo} src={photo} alt={`${p.fullName}, ${p.title}`} />
                  </div>
                  <div className={styles.ownerBody}>
                    <h3 className={styles.ownerName}>{p.fullName}</h3>
                    <p className={styles.ownerTitle}>{`${p.title} & Co-Founder`}</p>
                    <p className={styles.ownerBio}>{p.biography}</p>

                    {extra?.focus && (
                      <ul className={styles.focus}>
                        {extra.focus.map((f) => (
                          <li key={f} className={styles.focusItem}>{f}</li>
                        ))}
                      </ul>
                    )}

                    {extra?.education && extra.education.length > 0 && (
                      <div className={styles.detailBlock}>
                        <h4 className={styles.detailHeading}>Education</h4>
                        <ul className={styles.detailList}>
                          {extra.education.map((e) => (
                            <li key={e} className={styles.detailItem}>{e}</li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {extra?.experience && extra.experience.length > 0 && (
                      <div className={styles.detailBlock}>
                        <h4 className={styles.detailHeading}>Work experience</h4>
                        <ul className={styles.detailList}>
                          {extra.experience.map((e) => (
                            <li key={e} className={styles.detailItem}>{e}</li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {extra && (
                      <dl className={styles.contact}>
                        <div className={styles.contactRow}>
                          <dt className={styles.contactLabel}>Email</dt>
                          <dd className={styles.contactValue}>
                            <a href={`mailto:${extra.email}`}>{extra.email}</a>
                          </dd>
                        </div>
                        <div className={styles.contactRow}>
                          <dt className={styles.contactLabel}>Phone</dt>
                          <dd className={styles.contactValue}>{extra.phone}</dd>
                        </div>
                      </dl>
                    )}
                  </div>
                </article>
              )
            })}
          </div>
        </div>
      </section>

      <section className={styles.ctaBand} aria-label="Get in touch">
        <div className={styles.container}>
          <h2 className={styles.ctaHeading}>Let's work together.</h2>
          <p className={styles.ctaBody}>
            Tell us about your business and we'll show you how we can help.
          </p>
          <a className={styles.ctaLink} href="/contact">Contact us &#8594;</a>
        </div>
      </section>
    </main>
  )
}