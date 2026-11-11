import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../apiClient'
import styles from './HeroSection.module.css'

// ---------------------------------------------------------------------------
// HeroSection
// ---------------------------------------------------------------------------

/** Company name displayed as the hero heading (Requirement 2.1). */
const COMPANY_NAME = 'NNM Advisory'

/** Default CTA label used until/if the API value is unavailable (Requirement 2.1). */
const DEFAULT_CTA_LABEL = 'Contact Us'

/** Path to the brand logo asset served from the public/ directory. */
const LOGO_SRC = '/logo.svg'

/** Content key under which the tagline is stored in company_content. */
const TAGLINE_KEY = 'tagline'

/** Content key under which the CTA label is stored in company_content. */
const CTA_LABEL_KEY = 'hero_cta_label'

/**
 * A single configurable content item returned by `GET /api/profile`.
 * Mirrors the `CompanyContent` model: a key/value pair.
 */
interface CompanyContent {
  contentKey: string
  contentValue: string
}

/**
 * HeroSection renders the Home page hero (Requirements 2.1, 2.3, 2.5):
 *
 * - The company name "NNM Advisory" as the hero heading.
 * - A tagline (≤ 20 words per the design) fetched from `GET /api/profile`
 *   under the key `"tagline"`.
 * - A "Contact Us" CTA button (label from `"hero_cta_label"`, defaulting to
 *   "Contact Us") implemented as a react-router `Link` to `/contact`.
 * - The brand logo rendered at a minimum height of 48px. If the logo asset is
 *   unavailable, the `<img>` `onError` handler replaces it with the text
 *   "NNM Advisory" as a fallback (Requirement 2.5).
 *
 * The tagline and CTA label degrade gracefully: if the request fails or the
 * keys are absent, the tagline is simply omitted and the CTA falls back to its
 * default label, so the hero always renders something meaningful.
 */
export default function HeroSection() {
  const [tagline, setTagline] = useState<string>('')
  const [ctaLabel, setCtaLabel] = useState<string>(DEFAULT_CTA_LABEL)
  const [logoFailed, setLogoFailed] = useState<boolean>(false)

  useEffect(() => {
    let cancelled = false

    async function loadProfile() {
      const res = await apiGet<CompanyContent[]>('/api/profile')
      if (cancelled || !res.ok || !Array.isArray(res.data)) {
        return
      }

      const byKey = new Map(res.data.map((item) => [item.contentKey, item.contentValue]))

      const taglineValue = byKey.get(TAGLINE_KEY)
      if (taglineValue) {
        setTagline(taglineValue)
      }

      const ctaValue = byKey.get(CTA_LABEL_KEY)
      if (ctaValue) {
        setCtaLabel(ctaValue)
      }
    }

    void loadProfile()

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <section className={styles.hero} aria-label="Introduction">
      <div className={styles.container}>
        <div className={styles.logoWrapper}>
          {logoFailed ? (
            <span className={styles.logoFallback}>{COMPANY_NAME}</span>
          ) : (
            <img
              className={styles.logo}
              src={LOGO_SRC}
              alt={`${COMPANY_NAME} logo`}
              onError={() => setLogoFailed(true)}
            />
          )}
        </div>

        <h1 className={styles.title}>{COMPANY_NAME}</h1>

        {tagline && <p className={styles.tagline}>{tagline}</p>}

        <Link className={styles.cta} to="/contact">
          {ctaLabel}
        </Link>
      </div>
    </section>
  )
}
