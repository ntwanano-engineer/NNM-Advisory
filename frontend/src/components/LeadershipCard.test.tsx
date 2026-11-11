import { describe, it, expect } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import LeadershipCard, { type LeadershipProfile } from './LeadershipCard'

// ---------------------------------------------------------------------------
// Tests — Requirement 4.4
//
// The LeadershipCard must display a profile photo (from a pre-signed URL that
// may be null), full name, title, and biography. The photo is bounded to
// 200×200–800×800px via the CSS module. When the photo URL is missing or the
// image fails to load, an initials-based fallback avatar is shown so a broken
// image is never displayed.
// ---------------------------------------------------------------------------

const baseProfile: LeadershipProfile = {
  fullName: 'Ntwanano Mabasa',
  title: 'Chief Operating Officer (COO)',
  biography: 'An experienced operations leader with a background in advisory services.',
  photoUrl: 'https://example-bucket.s3.amazonaws.com/presigned/ntwanano.jpg?sig=abc',
}

describe('LeadershipCard', () => {
  it('displays the full name, title, and biography', () => {
    render(<LeadershipCard profile={baseProfile} />)
    expect(screen.getByRole('heading', { name: 'Ntwanano Mabasa' })).toBeInTheDocument()
    expect(screen.getByText('Chief Operating Officer (COO)')).toBeInTheDocument()
    expect(
      screen.getByText(/experienced operations leader/i)
    ).toBeInTheDocument()
  })

  it('renders the photo from the pre-signed URL with descriptive alt text', () => {
    render(<LeadershipCard profile={baseProfile} />)
    const img = screen.getByRole('img', { name: /Ntwanano Mabasa, Chief Operating Officer/i })
    expect(img).toHaveAttribute('src', baseProfile.photoUrl)
    // The sizing bounds (200–800px) are enforced via the CSS module .photo rule;
    // jsdom does not compute CSS-module styles, so we assert the image carries
    // its styling class rather than reading a computed size.
    expect(img.className).toBeTruthy()
  })

  it('shows the initials fallback when photoUrl is null', () => {
    render(<LeadershipCard profile={{ ...baseProfile, photoUrl: null }} />)
    expect(screen.queryByRole('img', { name: /Ntwanano Mabasa, Chief/i })).not.toBeInTheDocument()
    const fallback = screen.getByRole('img', { name: /Ntwanano Mabasa photo placeholder/i })
    expect(fallback).toBeInTheDocument()
    expect(screen.getByText('NM')).toBeInTheDocument()
  })

  it('shows the initials fallback when photoUrl is absent (undefined)', () => {
    const { photoUrl: _omitted, ...withoutPhoto } = baseProfile
    render(<LeadershipCard profile={withoutPhoto} />)
    expect(screen.getByRole('img', { name: /photo placeholder/i })).toBeInTheDocument()
    expect(screen.getByText('NM')).toBeInTheDocument()
  })

  it('swaps to the initials fallback when the image fails to load', () => {
    render(<LeadershipCard profile={baseProfile} />)
    const img = screen.getByRole('img', { name: /Ntwanano Mabasa, Chief/i })
    fireEvent.error(img)
    expect(screen.queryByRole('img', { name: /Ntwanano Mabasa, Chief/i })).not.toBeInTheDocument()
    expect(screen.getByRole('img', { name: /photo placeholder/i })).toBeInTheDocument()
    expect(screen.getByText('NM')).toBeInTheDocument()
  })

  it('derives a single initial for a single-word name', () => {
    render(<LeadershipCard profile={{ ...baseProfile, fullName: 'Ntwanano', photoUrl: null }} />)
    expect(screen.getByText('N')).toBeInTheDocument()
  })

  it('derives first and last initials for a multi-word name, ignoring middle names', () => {
    render(
      <LeadershipCard
        profile={{ ...baseProfile, fullName: 'Ntshovelo Priscilla Novela', photoUrl: null }}
      />
    )
    expect(screen.getByText('NN')).toBeInTheDocument()
  })
})
