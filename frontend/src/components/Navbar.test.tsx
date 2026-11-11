import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import Navbar from './Navbar'
import styles from './Navbar.module.css'

// ---------------------------------------------------------------------------
// Tests — Requirement 1.6
//
// The Navbar exposes a full set of links on desktop and a collapsible
// hamburger drawer on mobile (< 768px). The toggle button expands and
// collapses the drawer.
//
// jsdom limitation: jsdom does NOT evaluate CSS media queries or apply the
// stylesheet's layout rules. The "collapsible menu visible below 768px /
// full bar visible at ≥ 768px" distinction is enforced purely in
// Navbar.module.css (base styles vs. the `@media (min-width: 768px)` block)
// and therefore cannot be computed or asserted in jsdom. These tests instead
// assert the behavioural/state aspects that React controls: the toggle's
// aria state, the presence of the open/collapsed class on the link list, the
// toggle button's and link container's expected classes, and that all five
// navigation links render. Visual breakpoint behaviour must be verified via a
// real browser / visual or e2e test.
//
// Note: class assertions compare against the imported `styles` CSS-module
// object so they stay correct regardless of how the build hashes class names
// (the component and the test resolve the same identifiers).
// ---------------------------------------------------------------------------

const EXPECTED_LINKS = ['Home', 'Services', 'Mission', 'About Us', 'Contact Us']

function renderNavbar() {
  return render(
    <MemoryRouter>
      <Navbar />
    </MemoryRouter>
  )
}

function getToggle() {
  // aria-label flips between "Open navigation menu" and "Close navigation
  // menu" depending on state, so match either.
  return screen.getByRole('button', { name: /navigation menu/i })
}

function getLinkList() {
  return document.getElementById('primary-navigation') as HTMLElement
}

describe('Navbar', () => {
  it('renders the hamburger toggle button collapsed by default', () => {
    renderNavbar()

    const toggle = getToggle()
    expect(toggle).toBeInTheDocument()
    expect(toggle).toHaveAttribute('aria-label', 'Open navigation menu')
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-controls', 'primary-navigation')
    // The toggle carries its expected class so CSS can hide it at ≥ 768px.
    expect(toggle).toHaveClass(styles.toggle)
  })

  it('renders the link list (collapsed) with its expected class and no open class initially', () => {
    renderNavbar()

    const list = getLinkList()
    expect(list).toBeInTheDocument()
    expect(list.tagName).toBe('UL')
    expect(list).toHaveClass(styles.links)
    // Collapsed by default: the open modifier class is absent.
    expect(list).not.toHaveClass(styles.linksOpen)
  })

  it('expands the drawer when the toggle is activated', async () => {
    const user = userEvent.setup()
    renderNavbar()

    const toggle = getToggle()
    const list = getLinkList()

    await user.click(toggle)

    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(toggle).toHaveAttribute('aria-label', 'Close navigation menu')
    expect(list).toHaveClass(styles.linksOpen)
  })

  it('collapses the drawer when the toggle is activated a second time', async () => {
    const user = userEvent.setup()
    renderNavbar()

    const toggle = getToggle()
    const list = getLinkList()

    // Expand...
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(list).toHaveClass(styles.linksOpen)

    // ...then collapse.
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-label', 'Open navigation menu')
    expect(list).not.toHaveClass(styles.linksOpen)
  })

  it('renders all five navigation links', () => {
    renderNavbar()

    const list = getLinkList()
    for (const label of EXPECTED_LINKS) {
      expect(within(list).getByRole('link', { name: label })).toBeInTheDocument()
    }

    // Exactly the five expected links live in the primary navigation list.
    expect(within(list).getAllByRole('link')).toHaveLength(EXPECTED_LINKS.length)
  })

  it('points each navigation link at its expected route', () => {
    renderNavbar()

    const list = getLinkList()
    const expectedHrefs: Record<string, string> = {
      Home: '/',
      Services: '/services',
      Mission: '/mission',
      'About Us': '/about',
      'Contact Us': '/contact',
    }

    for (const [label, href] of Object.entries(expectedHrefs)) {
      expect(within(list).getByRole('link', { name: label })).toHaveAttribute(
        'href',
        href
      )
    }
  })

  it('collapses the drawer after a link is selected', async () => {
    const user = userEvent.setup()
    renderNavbar()

    const toggle = getToggle()
    const list = getLinkList()

    await user.click(toggle)
    expect(list).toHaveClass(styles.linksOpen)

    await user.click(within(list).getByRole('link', { name: 'About Us' }))

    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(list).not.toHaveClass(styles.linksOpen)
  })
})
