import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import HeroSection from './HeroSection'

// ---------------------------------------------------------------------------
// Tests — Requirements 2.1, 2.3, 2.5
//
// The HeroSection must display the company name "NNM Advisory", a tagline and
// CTA label fetched from GET /api/profile, a "Contact Us" CTA linking to
// /contact, a brand logo at ≥ 48px height, and a text fallback when the logo
// asset is unavailable.
// ---------------------------------------------------------------------------

// Mock the apiClient so no real network request is made.
const apiGet = vi.fn()
vi.mock('../apiClient', () => ({
  apiGet: (...args: unknown[]) => apiGet(...args),
}))

function renderHero() {
  return render(
    <MemoryRouter>
      <HeroSection />
    </MemoryRouter>
  )
}

/** Build a successful ApiResponse envelope for GET /api/profile. */
function profileResponse(items: Array<{ contentKey: string; contentValue: string }>) {
  return { ok: true, status: 200, data: items, errorMessage: null, timedOut: false }
}

describe('HeroSection', () => {
  beforeEach(() => {
    apiGet.mockReset()
  })

  it('displays the company name "NNM Advisory"', async () => {
    apiGet.mockResolvedValue(profileResponse([]))
    renderHero()
    expect(
      screen.getByRole('heading', { name: /NNM Advisory/i })
    ).toBeInTheDocument()
  })

  it('fetches and displays the tagline from GET /api/profile', async () => {
    apiGet.mockResolvedValue(
      profileResponse([{ contentKey: 'tagline', contentValue: 'Clarity for complex decisions.' }])
    )
    renderHero()
    await waitFor(() =>
      expect(screen.getByText('Clarity for complex decisions.')).toBeInTheDocument()
    )
    expect(apiGet).toHaveBeenCalledWith('/api/profile')
  })

  it('renders the CTA label from hero_cta_label as a link to /contact', async () => {
    apiGet.mockResolvedValue(
      profileResponse([{ contentKey: 'hero_cta_label', contentValue: 'Talk to us' }])
    )
    renderHero()
    await waitFor(() => {
      const cta = screen.getByRole('link', { name: 'Talk to us' })
      expect(cta).toHaveAttribute('href', '/contact')
    })
  })

  it('defaults the CTA label to "Contact Us" when hero_cta_label is absent', async () => {
    apiGet.mockResolvedValue(profileResponse([]))
    renderHero()
    const cta = screen.getByRole('link', { name: /Contact Us/i })
    expect(cta).toHaveAttribute('href', '/contact')
  })

  it('renders the brand logo carrying the styling class that enforces ≥ 48px height', async () => {
    // The 48px minimum height (Requirement 2.3) is enforced via the CSS module
    // `.logo` rule. jsdom does not compute CSS-module styles, so we assert the
    // logo image is rendered and carries its styling class rather than reading
    // a computed height.
    apiGet.mockResolvedValue(profileResponse([]))
    renderHero()
    const logo = screen.getByRole('img', { name: /NNM Advisory logo/i })
    expect(logo).toBeInTheDocument()
    expect(logo.className).toBeTruthy()
  })

  it('shows the "NNM Advisory" text fallback when the logo asset fails to load', async () => {
    apiGet.mockResolvedValue(profileResponse([]))
    renderHero()
    const logo = screen.getByRole('img', { name: /NNM Advisory logo/i })
    fireEvent.error(logo)
    await waitFor(() =>
      expect(screen.queryByRole('img', { name: /NNM Advisory logo/i })).not.toBeInTheDocument()
    )
    // Two "NNM Advisory" text nodes remain: the heading and the fallback.
    expect(screen.getAllByText(/NNM Advisory/i).length).toBeGreaterThanOrEqual(2)
  })

  it('still renders the hero when the profile request fails', async () => {
    apiGet.mockResolvedValue({
      ok: false,
      status: 500,
      data: null,
      errorMessage: 'Server error',
      timedOut: false,
    })
    renderHero()
    expect(screen.getByRole('heading', { name: /NNM Advisory/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Contact Us/i })).toHaveAttribute('href', '/contact')
  })
})
