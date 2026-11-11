import { Link } from 'react-router-dom'
import styles from './Footer.module.css'

// ---------------------------------------------------------------------------
// Footer
// ---------------------------------------------------------------------------

/** Company name displayed in the footer and copyright line. */
const COMPANY_NAME = 'NNM Advisory'

/** Contact email used for the mailto: social/contact link. */
const CONTACT_EMAIL = 'info@nnmadvisory.com'

/** LinkedIn company page (social link). */
const LINKEDIN_URL = 'https://www.linkedin.com/company/nnm-advisory'

/**
 * Footer renders the persistent site footer (Requirements 1.3, 1.4):
 * - The company name.
 * - A copyright notice using the current calendar year.
 * - At least one social/contact link — here an in-app Contact page link, a
 *   `mailto:` email link, and an external LinkedIn link.
 *
 * Responsiveness (Requirement 1.4): the layout uses flexbox with wrapping and
 * percentage/max-width sizing (see Footer.module.css) so it renders without
 * horizontal overflow or overlap from 320px up to 2560px viewport width.
 */
export default function Footer() {
  const year = new Date().getFullYear()

  return (
    <footer className={styles.footer} role="contentinfo">
      <div className={styles.container}>
        <div className={styles.brand}>
          <p className={styles.companyName}>{COMPANY_NAME}</p>
          <p className={styles.copyright}>
            &copy; {year} {COMPANY_NAME}. All rights reserved.
          </p>
        </div>

        <ul className={styles.links}>
          <li>
            <Link className={styles.link} to="/contact">
              Contact
            </Link>
          </li>
          <li>
            <a className={styles.link} href={`mailto:${CONTACT_EMAIL}`}>
              {CONTACT_EMAIL}
            </a>
          </li>
          <li>
            <a
              className={styles.link}
              href={LINKEDIN_URL}
              target="_blank"
              rel="noopener noreferrer"
            >
              LinkedIn
            </a>
          </li>
        </ul>
      </div>
    </footer>
  )
}
