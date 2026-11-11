import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import styles from './Navbar.module.css'

// ---------------------------------------------------------------------------
// Navbar
// ---------------------------------------------------------------------------

/** Company name / brand displayed on the left of the navigation bar. */
const BRAND_NAME = 'NNM Advisory'

/**
 * The persistent set of navigation links (Requirement 1.1):
 * Home, Services, About Us, Surveys, and Contact.
 */
const NAV_LINKS: ReadonlyArray<{ label: string; to: string }> = [
  { label: 'Home', to: '/' },
  { label: 'Services', to: '/services' },
  { label: 'Mission', to: '/mission' },
  { label: 'About Us', to: '/about' },
  { label: 'Contact Us', to: '/contact' },
]

/**
 * Navbar renders the persistent site navigation (Requirements 1.1, 1.6).
 *
 * Desktop (viewport width ≥ 768px):
 * - Displays the full horizontal navigation bar with all links visible.
 * - The hamburger toggle is hidden via CSS.
 *
 * Mobile (viewport width < 768px):
 * - The full link list is hidden by default and the hamburger toggle button
 *   is shown.
 * - Activating the toggle expands/collapses a drawer containing the links.
 *   Open/closed state is driven by React `useState`.
 *
 * Accessibility:
 * - The toggle button exposes `aria-label`, `aria-expanded`, and
 *   `aria-controls` so assistive technology can announce its purpose and
 *   current state.
 * - The link list is given an `id` referenced by `aria-controls`.
 * - Selecting a link closes the mobile drawer so the menu does not stay open
 *   after navigation.
 *
 * `NavLink` applies an "active" class to the link matching the current route,
 * and React Router handles navigation without a full page reload.
 */
export default function Navbar() {
  const [isOpen, setIsOpen] = useState(false)

  const toggleMenu = () => setIsOpen((open) => !open)
  const closeMenu = () => setIsOpen(false)

  const linkClassName = ({ isActive }: { isActive: boolean }) =>
    isActive ? `${styles.link} ${styles.linkActive}` : styles.link

  return (
    <nav className={styles.navbar} aria-label="Primary">
      <div className={styles.container}>
        <NavLink to="/" className={styles.brand} onClick={closeMenu}>
          {BRAND_NAME}
        </NavLink>

        <button
          type="button"
          className={styles.toggle}
          aria-label={isOpen ? 'Close navigation menu' : 'Open navigation menu'}
          aria-expanded={isOpen}
          aria-controls="primary-navigation"
          onClick={toggleMenu}
        >
          <span className={styles.toggleBar} aria-hidden="true" />
          <span className={styles.toggleBar} aria-hidden="true" />
          <span className={styles.toggleBar} aria-hidden="true" />
        </button>

        <ul
          id="primary-navigation"
          className={`${styles.links} ${isOpen ? styles.linksOpen : ''}`}
        >
          {NAV_LINKS.map(({ label, to }) => (
            <li key={to} className={styles.linkItem}>
              <NavLink
                to={to}
                end={to === '/'}
                className={linkClassName}
                onClick={closeMenu}
              >
                {label}
              </NavLink>
            </li>
          ))}
        </ul>
      </div>
    </nav>
  )
}
