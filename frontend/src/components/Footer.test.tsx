import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import Footer from './Footer'

// ---------------------------------------------------------------------------
// Tests — Requirements 1.3, 1.4
//
// The Footer must contain the company name, a copyright notice (with the
// current year), and at least one social/contact link.
// ---------------------------------------------------------------------------

function renderFooter() {
  return render(
    <MemoryRouter>
      <Footer />
    </MemoryRouter>
  )
}

describe('Footer', () => {
  it('renders the company name', () => {
    renderFooter()
    const footer = screen.getByRole('contentinfo')
    expect(within(footer).getAllByText(/NNM Advisory/i).length).toBeGreaterThan(0)
  })

  it('renders a copyright notice with the current year', () => {
    renderFooter()
    const year = new Date().getFullYear()
    const copyright = screen.getByText(
      new RegExp(`©\\s*${year}\\s*NNM Advisory. All rights reserved.`, 'i')
    )
    expect(copyright).toBeInTheDocument()
  })

  it('renders at least one social/contact link', () => {
    renderFooter()
    const footer = screen.getByRole('contentinfo')
    const links = within(footer).getAllByRole('link')
    expect(links.length).toBeGreaterThanOrEqual(1)
  })

  it('includes an in-app Contact link pointing to /contact', () => {
    renderFooter()
    const contactLink = screen.getByRole('link', { name: /contact/i })
    expect(contactLink).toHaveAttribute('href', '/contact')
  })

  it('includes a mailto contact link', () => {
    renderFooter()
    const mailtoLink = screen
      .getAllByRole('link')
      .find((a) => a.getAttribute('href')?.startsWith('mailto:'))
    expect(mailtoLink).toBeDefined()
  })

  it('includes an external social (LinkedIn) link opened safely in a new tab', () => {
    renderFooter()
    const linkedin = screen.getByRole('link', { name: /linkedin/i })
    expect(linkedin).toHaveAttribute('target', '_blank')
    expect(linkedin).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })
})
