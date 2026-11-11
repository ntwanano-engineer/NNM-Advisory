import { useState, type FormEvent } from 'react'
import { useForm } from '../hooks/useForm'
import styles from './ContactForm.module.css'

// ---------------------------------------------------------------------------
// ContactForm (Requirements 5.1, 5.6, 5.7, 5.8, 5.9, 5.10)
// ---------------------------------------------------------------------------

/** API path for contact form submissions (design: POST /api/contact). */
const CONTACT_ENDPOINT = '/api/contact'

/** Field length limits (Requirement 5.1). */
const LIMITS = {
  fullName: 100,
  email: 254,
  subject: 150,
  messageMin: 10,
  messageMax: 2000,
} as const

/**
 * Standard email format check (Requirement 5.7): local-part@domain.tld.
 *
 * Intentionally pragmatic rather than fully RFC 5322 compliant — it requires a
 * non-empty local part, a single `@`, a domain, and a dotted TLD of at least
 * two characters, while disallowing whitespace. The authoritative validation
 * happens server-side; this is a fast client-side guard to avoid an obviously
 * invalid round-trip.
 */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/

/** The four controlled fields in the contact form. */
interface ContactFields {
  fullName: string
  email: string
  subject: string
  message: string
}

/** Per-field validation error messages; a missing key means the field is valid. */
type ContactErrors = Partial<Record<keyof ContactFields, string>>

const EMPTY_FIELDS: ContactFields = {
  fullName: '',
  email: '',
  subject: '',
  message: '',
}

/** Success response shape from POST /api/contact (202 Accepted). */
interface ContactSuccessResponse {
  message: string
}

/** Default confirmation copy used if the server omits a message. */
const DEFAULT_SUCCESS_MESSAGE =
  'Your message has been received. We will be in touch shortly.'

/**
 * Validate all fields against the Requirement 5.1 constraints plus the email
 * format rule (Requirement 5.7). Returns a map of field → error message for any
 * field that is invalid; an empty object means the form is valid.
 */
function validate(fields: ContactFields): ContactErrors {
  const errors: ContactErrors = {}

  const fullName = fields.fullName.trim()
  if (!fullName) {
    errors.fullName = 'Full name is required.'
  } else if (fullName.length > LIMITS.fullName) {
    errors.fullName = `Full name must be ${LIMITS.fullName} characters or fewer.`
  }

  const email = fields.email.trim()
  if (!email) {
    errors.email = 'Email address is required.'
  } else if (email.length > LIMITS.email) {
    errors.email = `Email address must be ${LIMITS.email} characters or fewer.`
  } else if (!EMAIL_PATTERN.test(email)) {
    errors.email = 'Enter a valid email address (e.g. name@example.com).'
  }

  const subject = fields.subject.trim()
  if (!subject) {
    errors.subject = 'Subject is required.'
  } else if (subject.length > LIMITS.subject) {
    errors.subject = `Subject must be ${LIMITS.subject} characters or fewer.`
  }

  const message = fields.message.trim()
  if (!message) {
    errors.message = 'Message is required.'
  } else if (message.length < LIMITS.messageMin) {
    errors.message = `Message must be at least ${LIMITS.messageMin} characters.`
  } else if (message.length > LIMITS.messageMax) {
    errors.message = `Message must be ${LIMITS.messageMax} characters or fewer.`
  }

  return errors
}

/**
 * ContactForm renders the Contact section's form with controlled inputs,
 * inline client-side validation, and submission handling via `useForm`.
 *
 * Behaviour:
 * - Invalid fields show an inline error adjacent to the field and the form is
 *   NOT submitted to the API (Requirements 5.6, 5.7).
 * - While a request is in flight the submit button shows a loading state.
 * - On a 2xx success response the form is replaced with a confirmation message
 *   (Requirement 5.8).
 * - On a non-2xx error response an error banner is shown and field values are
 *   preserved (Requirement 5.9).
 * - On a 30-second timeout a timeout message is shown and field values are
 *   preserved (Requirement 5.10).
 */
export default function ContactForm() {
  const [fields, setFields] = useState<ContactFields>(EMPTY_FIELDS)
  const [errors, setErrors] = useState<ContactErrors>({})
  const form = useForm<ContactSuccessResponse>()

  function handleChange(field: keyof ContactFields) {
    return (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      const value = event.target.value
      setFields((prev) => ({ ...prev, [field]: value }))
      // Clear a field's error as soon as the user edits it for responsive feedback.
      setErrors((prev) => {
        if (!prev[field]) return prev
        const next = { ...prev }
        delete next[field]
        return next
      })
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const validationErrors = validate(fields)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) {
      // Requirements 5.6 / 5.7: do not submit while any field is invalid.
      return
    }

    await form.submit(CONTACT_ENDPOINT, {
      fullName: fields.fullName.trim(),
      email: fields.email.trim(),
      subject: fields.subject.trim(),
      message: fields.message.trim(),
    })
  }

  // Requirement 5.8: on success, replace the form with a confirmation message.
  if (form.isSuccess) {
    const confirmation = form.response?.data?.message ?? DEFAULT_SUCCESS_MESSAGE
    return (
      <div className={styles.successMessage} role="status" aria-live="polite">
        <h2 className={styles.successHeading}>Message sent</h2>
        <p>{confirmation}</p>
      </div>
    )
  }

  const banner = form.isTimeout
    ? {
        // Requirement 5.10: timeout message, fields preserved.
        heading: 'Request timed out',
        text:
          form.errorMessage ??
          'The request timed out. Please check your connection and try again later.',
      }
    : form.isError
      ? {
          // Requirement 5.9: error message, fields preserved.
          heading: 'Something went wrong',
          text:
            form.errorMessage ??
            'We could not send your message. Please try again later.',
        }
      : null

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate>
      {banner && (
        <div className={styles.errorBanner} role="alert">
          <strong className={styles.errorBannerHeading}>{banner.heading}</strong>
          <span>{banner.text}</span>
        </div>
      )}

      <div className={styles.field}>
        <label className={styles.label} htmlFor="contact-full-name">
          Full Name
        </label>
        <input
          id="contact-full-name"
          name="fullName"
          type="text"
          className={styles.input}
          value={fields.fullName}
          maxLength={LIMITS.fullName}
          onChange={handleChange('fullName')}
          disabled={form.isLoading}
          aria-invalid={errors.fullName ? true : undefined}
          aria-describedby={errors.fullName ? 'contact-full-name-error' : undefined}
        />
        {errors.fullName && (
          <p id="contact-full-name-error" className={styles.fieldError} role="alert">
            {errors.fullName}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="contact-email">
          Email Address
        </label>
        <input
          id="contact-email"
          name="email"
          type="email"
          className={styles.input}
          value={fields.email}
          maxLength={LIMITS.email}
          onChange={handleChange('email')}
          disabled={form.isLoading}
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={errors.email ? 'contact-email-error' : undefined}
        />
        {errors.email && (
          <p id="contact-email-error" className={styles.fieldError} role="alert">
            {errors.email}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="contact-subject">
          Subject
        </label>
        <input
          id="contact-subject"
          name="subject"
          type="text"
          className={styles.input}
          value={fields.subject}
          maxLength={LIMITS.subject}
          onChange={handleChange('subject')}
          disabled={form.isLoading}
          aria-invalid={errors.subject ? true : undefined}
          aria-describedby={errors.subject ? 'contact-subject-error' : undefined}
        />
        {errors.subject && (
          <p id="contact-subject-error" className={styles.fieldError} role="alert">
            {errors.subject}
          </p>
        )}
      </div>

      <div className={styles.field}>
        <label className={styles.label} htmlFor="contact-message">
          Message
        </label>
        <textarea
          id="contact-message"
          name="message"
          className={styles.textarea}
          value={fields.message}
          maxLength={LIMITS.messageMax}
          rows={6}
          onChange={handleChange('message')}
          disabled={form.isLoading}
          aria-invalid={errors.message ? true : undefined}
          aria-describedby={errors.message ? 'contact-message-error' : undefined}
        />
        {errors.message && (
          <p id="contact-message-error" className={styles.fieldError} role="alert">
            {errors.message}
          </p>
        )}
      </div>

      <button type="submit" className={styles.submit} disabled={form.isLoading}>
        {form.isLoading ? 'Sending…' : 'Send Message'}
      </button>
    </form>
  )
}
