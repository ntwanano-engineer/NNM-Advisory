import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import HomePage from './HomePage'

// ---------------------------------------------------------------------------
// Tests — Requirements 2.2, 3.8
//
// HomePage renders the HeroSection plus 2–6 service area summaries fetched from
// GET /api/profile/services. On a successful fetch the response is cached in
// localStorage; when the API is unavailable the summaries are rendered from
// that cache so they are never empty.
// ---------------------------------------------------------------------------

const apiGet = vi.fn()
vi.mock('../apiClient', () => ({
  apiGet: (...args: unknown[]) => apiGet(...args),
}))

const SERVICES_CACHE_KEY = 'nnm:services'

interface ServiceBlock {
  id: string
  category: string
  title: string
  description: string
  offerings: string[]
}

function makeService(n: number): ServiceBlock {
  return {
    id: `id-${n}`,
    category: `cat-${n}`,
    title: `Service ${n}`,
    description: `Description for service ${n}.`,
    offerings: [`offering-${n}`],
  }
}

function servicesResponse(items: ServiceBlock[]) {
  return { ok: true, status: 200, data: items, errorMessage: null, timedOut: false }
}

function errorResponse() {
  return {
    ok: false,
    status: 500,
    data: null,
    errorMessage: 'Server error',
    timedOut: false,
  }
}

function renderHome() {
  return render(
    <MemoryRouter>
      <HomePage />
    </MemoryRouter>,
  )
}

describe('HomePage', () => {
  beforeEach(() => {
    apiGet.mockReset()
    localStorage.clear()
  })

  it('renders the HeroSection (company name heading)', async () => {
    apiGet.mockResolvedValue(servicesResponse([makeService(1), makeService(2)]))
    renderHome()
    expect(
      screen.getByRole('heading', { name: /NNM Advisory/i }),
    ).toBeInTheDocument()
  })

  it('renders service area summaries fetched from GET /api/profile/services', async () => {
    const services = [makeService(1), makeService(2), makeService(3)]
    apiGet.mockResolvedValue(servicesResponse(services))
    renderHome()

    await waitFor(() => expect(screen.getByText('Service 1')).toBeInTheDocument())
    expect(screen.getByText('Service 2')).toBeInTheDocument()
    expect(screen.getByText('Service 3')).toBeInTheDocument()
    expect(screen.getByText('Description for service 1.')).toBeInTheDocument()
    expect(apiGet).toHaveBeenCalledWith('/api/profile/services')
  })

  it('caches the successful response in localStorage under "nnm:services"', async () => {
    const services = [makeService(1), makeService(2)]
    apiGet.mockResolvedValue(servicesResponse(services))
    renderHome()

    await waitFor(() => expect(screen.getByText('Service 1')).toBeInTheDocument())
    const cached = JSON.parse(localStorage.getItem(SERVICES_CACHE_KEY) ?? 'null')
    expect(cached).toHaveLength(2)
    expect(cached[0].title).toBe('Service 1')
  })

  it('truncates to a maximum of 6 service areas', async () => {
    const services = Array.from({ length: 8 }, (_, i) => makeService(i + 1))
    apiGet.mockResolvedValue(servicesResponse(services))
    renderHome()

    await waitFor(() => expect(screen.getByText('Service 1')).toBeInTheDocument())
    const region = screen.getByRole('region', { name: /service areas/i })
    const titles = within(region).getAllByRole('heading', { level: 3 })
    expect(titles).toHaveLength(6)
    expect(screen.queryByText('Service 7')).not.toBeInTheDocument()
  })

  it('renders from the localStorage cache when the API is unavailable', async () => {
    const cached = [makeService(1), makeService(2)]
    localStorage.setItem(SERVICES_CACHE_KEY, JSON.stringify(cached))
    apiGet.mockResolvedValue(errorResponse())

    renderHome()

    await waitFor(() => expect(screen.getByText('Service 1')).toBeInTheDocument())
    expect(screen.getByText('Service 2')).toBeInTheDocument()
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('omits the services section when the API fails and no cache exists', async () => {
    apiGet.mockResolvedValue(errorResponse())
    renderHome()

    // Hero still renders.
    expect(screen.getByRole('heading', { name: /NNM Advisory/i })).toBeInTheDocument()
    // No service-areas section.
    await waitFor(() =>
      expect(screen.queryByRole('region', { name: /service areas/i })).not.toBeInTheDocument(),
    )
  })
})
