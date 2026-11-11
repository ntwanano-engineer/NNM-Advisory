import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import ServicesPage from './ServicesPage'

// ---------------------------------------------------------------------------
// Tests — Requirements 3.1–3.5, 3.8
//
// ServicesPage renders a ServiceBlock for every category returned by
// GET /api/profile/services (accounting, it, customer_survey, employee_survey),
// each with title, description, and its offerings as distinct list items.
// A successful response is cached in localStorage; when the API is unavailable
// the page renders from that cache, or a static fallback of the four blocks,
// so the blocks are never empty.
// ---------------------------------------------------------------------------

const apiGet = vi.fn()
vi.mock('../apiClient', () => ({
  apiGet: (...args: unknown[]) => apiGet(...args),
}))

const SERVICES_CACHE_KEY = 'nnm:services'

interface ServiceBlockData {
  id: string
  category: string
  title: string
  description: string
  offerings: string[]
}

const FOUR_BLOCKS: ServiceBlockData[] = [
  {
    id: 'acc',
    category: 'accounting',
    title: 'Accounting Services',
    description: 'Accounting description.',
    offerings: ['Bookkeeping', 'Tax services', 'Financial analysis and insights'],
  },
  {
    id: 'it',
    category: 'it',
    title: 'IT Services',
    description: 'IT description.',
    offerings: [
      'Financial system setup for payroll and HR',
      'Website development',
      'IT infrastructure',
    ],
  },
  {
    id: 'cust',
    category: 'customer_survey',
    title: 'Customer Surveys',
    description: 'Customer survey description.',
    offerings: [],
  },
  {
    id: 'emp',
    category: 'employee_survey',
    title: 'Employee Surveys',
    description: 'Employee survey description.',
    offerings: [],
  },
]

function okResponse(items: ServiceBlockData[]) {
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

function renderServices() {
  return render(
    <MemoryRouter>
      <ServicesPage />
    </MemoryRouter>,
  )
}

describe('ServicesPage', () => {
  beforeEach(() => {
    apiGet.mockReset()
    localStorage.clear()
  })

  it('fetches from GET /api/profile/services and renders all four blocks', async () => {
    apiGet.mockResolvedValue(okResponse(FOUR_BLOCKS))
    renderServices()

    await waitFor(() =>
      expect(screen.getByText('Accounting Services')).toBeInTheDocument(),
    )
    expect(screen.getByText('IT Services')).toBeInTheDocument()
    expect(screen.getByText('Customer Surveys')).toBeInTheDocument()
    expect(screen.getByText('Employee Surveys')).toBeInTheDocument()
    expect(apiGet).toHaveBeenCalledWith('/api/profile/services')
  })

  it('renders each offering as a distinct list item', async () => {
    apiGet.mockResolvedValue(okResponse(FOUR_BLOCKS))
    renderServices()

    const accounting = await screen.findByRole('article', {
      name: 'Accounting Services',
    })
    const accountingItems = within(accounting).getAllByRole('listitem')
    expect(accountingItems.map((li) => li.textContent)).toEqual([
      'Bookkeeping',
      'Tax services',
      'Financial analysis and insights',
    ])

    const it = screen.getByRole('article', { name: 'IT Services' })
    const itItems = within(it).getAllByRole('listitem')
    expect(itItems.map((li) => li.textContent)).toEqual([
      'Financial system setup for payroll and HR',
      'Website development',
      'IT infrastructure',
    ])
  })

  it('shows survey block descriptions without an offerings list', async () => {
    apiGet.mockResolvedValue(okResponse(FOUR_BLOCKS))
    renderServices()

    // Wait for the API data to replace the seeded fallback copy.
    await screen.findByText('Customer survey description.')

    const customer = screen.getByRole('article', { name: 'Customer Surveys' })
    expect(
      within(customer).getByText('Customer survey description.'),
    ).toBeInTheDocument()
    expect(within(customer).queryByRole('list')).not.toBeInTheDocument()
  })

  it('caches the successful response in localStorage under "nnm:services"', async () => {
    apiGet.mockResolvedValue(okResponse(FOUR_BLOCKS))
    renderServices()

    await waitFor(() =>
      expect(screen.getByText('Accounting Services')).toBeInTheDocument(),
    )
    const cached = JSON.parse(localStorage.getItem(SERVICES_CACHE_KEY) ?? 'null')
    expect(cached).toHaveLength(4)
    expect(cached[0].title).toBe('Accounting Services')
  })

  it('renders from the localStorage cache when the API is unavailable', async () => {
    localStorage.setItem(SERVICES_CACHE_KEY, JSON.stringify(FOUR_BLOCKS))
    apiGet.mockResolvedValue(errorResponse())
    renderServices()

    await waitFor(() =>
      expect(screen.getByText('Accounting Services')).toBeInTheDocument(),
    )
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('renders the four static fallback blocks when the API fails and no cache exists', async () => {
    apiGet.mockResolvedValue(errorResponse())
    renderServices()

    // The blocks are never empty: the four categories are present via fallback.
    await waitFor(() =>
      expect(screen.getByText('Accounting Services')).toBeInTheDocument(),
    )
    expect(screen.getByText('IT Services')).toBeInTheDocument()
    expect(screen.getByText('Customer Surveys')).toBeInTheDocument()
    expect(screen.getByText('Employee Surveys')).toBeInTheDocument()
    // Fallback offerings are rendered as list items.
    expect(screen.getByText('Bookkeeping')).toBeInTheDocument()
    expect(screen.getByText('IT infrastructure')).toBeInTheDocument()
  })
})
