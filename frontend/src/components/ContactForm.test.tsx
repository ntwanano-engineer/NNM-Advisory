import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ContactForm from './ContactForm'

// ---------------------------------------------------------------------------
// Tests — Requirements 5.6, 5.7, 5.8, 5.9
//
// ContactForm renders four controlled fields (Full Name, Email, Subject,
// Message) with inline client-side validation and submits via the useForm hook
// which calls apiPost. These tests cover:
//   5.6 — empty fields show inline errors and the form is NOT submitted
//   5.7 — an invalid email shows the email error and the form is NOT submitted
//   5.8 — a successful (202) submission shows the confirmation message
//   5.9 — a server error (500) shows an error banner and preserves field values
// ---------------------------------------------------------------------------

const apiPost = vi.fn()
vi.mock('../apiClient', () => ({
  apiPost: (...args: unknown[]) => apiPost(...args),
}))

interface ContactEnvelope {
  ok: boolean
  status: number
  data: { message: string } | null
  errorMessage: string | null
  timedOut: boolean
}

function successResponse(message = 'Thanks, we will be in touch.'): ContactEnvelope {
  return { ok: true, status: 202, data: { message }, errorMessage: null, timedOut: false }
}

function serverErrorResponse(): ContactEnvelope {
  return {
    ok: false,
    status: 500,
    data: null,
    errorMessage: 'Something went wrong on our end. Please try again later.',
    timedOut: false,
  }
}

/** Fill all four fields with valid values. */
async function fillValidForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Full Name'), 'Jane Doe')
  await user.type(screen.getByLabelText('Email Address'), 'jane@example.com')
  await user.type(screen.getByLabelText('Subject'), 'Consulting enquiry')
  await user.type(
    screen.getByLabelText('Message'),
    'I would like to learn more about your services.',
  )
}

describe('ContactForm', () => {
  beforeEach(() => {
    apiPost.mockReset()
  })

  it('shows inline validation errors and does not submit when fields are empty (Req 5.6)', async () => {
    const user = userEvent.setup()
    render(<ContactForm />)

    await user.click(screen.getByRole('button', { name: 'Send Message' }))

    expect(screen.getByText('Full name is required.')).toBeInTheDocument()
    expect(screen.getByText('Email address is required.')).toBeInTheDocument()
    expect(screen.getByText('Subject is required.')).toBeInTheDocument()
    expect(screen.getByText('Message is required.')).toBeInTheDocument()
    expect(apiPost).not.toHaveBeenCalled()
  })

  it('shows the email error and does not submit when the email is invalid (Req 5.7)', async () => {
    const user = userEvent.setup()
    render(<ContactForm />)

    await user.type(screen.getByLabelText('Full Name'), 'Jane Doe')
    await user.type(screen.getByLabelText('Email Address'), 'not-an-email')
    await user.type(screen.getByLabelText('Subject'), 'Consulting enquiry')
    await user.type(
      screen.getByLabelText('Message'),
      'I would like to learn more about your services.',
    )

    await user.click(screen.getByRole('button', { name: 'Send Message' }))

    expect(
      screen.getByText('Enter a valid email address (e.g. name@example.com).'),
    ).toBeInTheDocument()
    expect(apiPost).not.toHaveBeenCalled()
  })

  it('replaces the form with the confirmation message on a successful submission (Req 5.8)', async () => {
    const user = userEvent.setup()
    apiPost.mockResolvedValue(successResponse('Thanks, we will be in touch.'))
    render(<ContactForm />)

    await fillValidForm(user)
    await user.click(screen.getByRole('button', { name: 'Send Message' }))

    await waitFor(() =>
      expect(screen.getByText('Message sent')).toBeInTheDocument(),
    )
    expect(screen.getByText('Thanks, we will be in touch.')).toBeInTheDocument()
    // The form is gone — no submit button remains.
    expect(
      screen.queryByRole('button', { name: 'Send Message' }),
    ).not.toBeInTheDocument()
    expect(apiPost).toHaveBeenCalledTimes(1)
    expect(apiPost).toHaveBeenCalledWith('/api/contact', {
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      subject: 'Consulting enquiry',
      message: 'I would like to learn more about your services.',
    })
  })

  it('shows an error banner and preserves field values on a server error (Req 5.9)', async () => {
    const user = userEvent.setup()
    apiPost.mockResolvedValue(serverErrorResponse())
    render(<ContactForm />)

    await fillValidForm(user)
    await user.click(screen.getByRole('button', { name: 'Send Message' }))

    // Error banner is shown.
    await waitFor(() =>
      expect(screen.getByText('Something went wrong')).toBeInTheDocument(),
    )
    expect(
      screen.getByText('Something went wrong on our end. Please try again later.'),
    ).toBeInTheDocument()

    // Field values are preserved (inputs still hold the typed values).
    expect(screen.getByLabelText('Full Name')).toHaveValue('Jane Doe')
    expect(screen.getByLabelText('Email Address')).toHaveValue('jane@example.com')
    expect(screen.getByLabelText('Subject')).toHaveValue('Consulting enquiry')
    expect(screen.getByLabelText('Message')).toHaveValue(
      'I would like to learn more about your services.',
    )
  })
})
