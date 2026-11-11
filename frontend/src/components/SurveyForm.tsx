import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import styles from './SurveyForm.module.css'

// ---------------------------------------------------------------------------
// SurveyForm — shared base component (Requirements 6.4, 7.4, 7.8)
// ---------------------------------------------------------------------------
//
// A generic, config-driven survey form used by both the Customer and Employee
// survey pages (tasks 18.2 / 18.3). It owns the answer state and the
// client-side required-question validation, and renders two input kinds:
//
//   - `rating` — a discrete scale (e.g. 1–5, 0–10) rendered as radio buttons.
//   - `text`   — an open-ended textarea with an optional `maxLength`.
//
// Behaviour defined by the acceptance criteria:
//   - Required questions are visibly indicated before any submit attempt
//     (Requirement 7.8) via an asterisk and a "Required" hint.
//   - On an attempted submit with one or more unanswered required questions,
//     each such question is highlighted with an inline error and the form does
//     NOT invoke `onSubmit` — so no API call is made (Requirements 6.4, 7.4).
//
// The component is intentionally presentation + validation only. The API wiring
// (loading / success / error / timeout) is the responsibility of the consuming
// page via the `onSubmit` callback, keeping SurveyForm reusable.

/** A single rating-scale question (e.g. 1–5 satisfaction, 0–10 NPS). */
export interface RatingQuestion {
  /** Stable identifier used as the answer-map key. */
  id: string
  /** Question label shown to the respondent. */
  label: string
  type: 'rating'
  /** Whether a response is required for submission. */
  required: boolean
  /** Lowest selectable value (inclusive). */
  min: number
  /** Highest selectable value (inclusive). */
  max: number
  /** Optional caption shown beneath the lowest scale value. */
  minLabel?: string
  /** Optional caption shown beneath the highest scale value. */
  maxLabel?: string
  /** Optional helper/description text shown under the label. */
  description?: string
}

/** A single open-ended text question. */
export interface TextQuestion {
  /** Stable identifier used as the answer-map key. */
  id: string
  /** Question label shown to the respondent. */
  label: string
  type: 'text'
  /** Whether a response is required for submission. */
  required: boolean
  /** Optional maximum character length enforced on the textarea. */
  maxLength?: number
  /** Optional placeholder text. */
  placeholder?: string
  /** Optional helper/description text shown under the label. */
  description?: string
}

/** Union of all supported question kinds. */
export type SurveyQuestion = RatingQuestion | TextQuestion

/**
 * The collected answers keyed by question id.
 *
 * Rating answers are stored as numbers, text answers as strings. A missing key
 * (or an empty/whitespace-only string) counts as unanswered for validation.
 */
export type SurveyAnswers = Record<string, number | string>

/** Per-question validation error messages; a missing key means the answer is valid. */
export type SurveyErrors = Record<string, string>

export interface SurveyFormProps {
  /** Ordered list of questions to render. */
  questions: SurveyQuestion[]
  /**
   * Called with the collected answers only when all required questions are
   * answered. Consumers wire this to the API; SurveyForm never calls the API
   * itself. May be async so the caller can await the submission.
   */
  onSubmit: (answers: SurveyAnswers) => void | Promise<void>
  /** Label for the submit button in its idle state. Defaults to "Submit Survey". */
  submitLabel?: string
  /** When true, inputs and the submit button are disabled (e.g. while submitting). */
  isSubmitting?: boolean
  /** Optional content rendered above the questions (e.g. intro copy, banners). */
  children?: ReactNode
}

/** Hint text shown next to every required question's label (Requirement 7.8). */
const REQUIRED_HINT = 'Required'

/**
 * Determine whether an answer value counts as "provided".
 *
 * - Rating answers are provided when a number has been selected.
 * - Text answers are provided when they contain at least one non-whitespace
 *   character.
 */
function isAnswered(value: number | string | undefined): boolean {
  if (typeof value === 'number') {
    return Number.isFinite(value)
  }
  if (typeof value === 'string') {
    return value.trim().length > 0
  }
  return false
}

/**
 * Validate the answers against the question config. Returns a map of
 * questionId → error message for every required question left unanswered; an
 * empty object means the form is valid (Requirements 6.4, 7.4).
 */
function validate(questions: SurveyQuestion[], answers: SurveyAnswers): SurveyErrors {
  const errors: SurveyErrors = {}
  for (const question of questions) {
    if (question.required && !isAnswered(answers[question.id])) {
      errors[question.id] = 'This question requires a response.'
    }
  }
  return errors
}

/**
 * SurveyForm renders a configurable set of rating and text questions, owns the
 * answer state, and performs client-side required-question validation on
 * submit.
 */
export default function SurveyForm({
  questions,
  onSubmit,
  submitLabel = 'Submit Survey',
  isSubmitting = false,
  children,
}: SurveyFormProps) {
  const [answers, setAnswers] = useState<SurveyAnswers>({})
  const [errors, setErrors] = useState<SurveyErrors>({})

  // Stable value range arrays for each rating question, recomputed only when
  // the question set changes.
  const ratingRanges = useMemo(() => {
    const ranges: Record<string, number[]> = {}
    for (const question of questions) {
      if (question.type === 'rating') {
        const values: number[] = []
        for (let v = question.min; v <= question.max; v += 1) {
          values.push(v)
        }
        ranges[question.id] = values
      }
    }
    return ranges
  }, [questions])

  function clearError(id: string) {
    setErrors((prev) => {
      if (!prev[id]) return prev
      const next = { ...prev }
      delete next[id]
      return next
    })
  }

  function handleRatingChange(id: string, value: number) {
    setAnswers((prev) => ({ ...prev, [id]: value }))
    clearError(id)
  }

  function handleTextChange(id: string, value: string) {
    setAnswers((prev) => ({ ...prev, [id]: value }))
    if (value.trim().length > 0) {
      clearError(id)
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const validationErrors = validate(questions, answers)
    setErrors(validationErrors)
    if (Object.keys(validationErrors).length > 0) {
      // Requirements 6.4 / 7.4: do not submit while any required question is
      // unanswered. No API call is made — control never reaches onSubmit.
      return
    }

    await onSubmit(answers)
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate>
      {children}

      {questions.map((question) => {
        const error = errors[question.id]
        const errorId = `${question.id}-error`
        const describedBy = [
          question.description ? `${question.id}-desc` : null,
          error ? errorId : null,
        ]
          .filter(Boolean)
          .join(' ') || undefined

        return (
          <fieldset
            key={question.id}
            className={styles.field}
            aria-invalid={error ? true : undefined}
            aria-describedby={describedBy}
          >
            <legend className={styles.legend}>
              <span className={styles.labelText}>{question.label}</span>
              {question.required && (
                <span className={styles.requiredHint}>
                  <span aria-hidden="true" className={styles.asterisk}>
                    *
                  </span>
                  <span className={styles.requiredText}>{REQUIRED_HINT}</span>
                </span>
              )}
            </legend>

            {question.description && (
              <p id={`${question.id}-desc`} className={styles.description}>
                {question.description}
              </p>
            )}

            {question.type === 'rating' ? (
              <div
                className={styles.ratingScale}
                role="radiogroup"
                aria-label={question.label}
              >
                {ratingRanges[question.id].map((value) => {
                  const inputId = `${question.id}-${value}`
                  const selected = answers[question.id] === value
                  return (
                    <label
                      key={inputId}
                      htmlFor={inputId}
                      className={`${styles.ratingOption} ${
                        selected ? styles.ratingOptionSelected : ''
                      }`}
                    >
                      <input
                        id={inputId}
                        type="radio"
                        name={question.id}
                        className={styles.ratingInput}
                        value={value}
                        checked={selected}
                        disabled={isSubmitting}
                        onChange={() => handleRatingChange(question.id, value)}
                      />
                      <span className={styles.ratingValue}>{value}</span>
                      {value === question.min && question.minLabel && (
                        <span className={styles.ratingEndLabel}>
                          {question.minLabel}
                        </span>
                      )}
                      {value === question.max && question.maxLabel && (
                        <span className={styles.ratingEndLabel}>
                          {question.maxLabel}
                        </span>
                      )}
                    </label>
                  )
                })}
              </div>
            ) : (
              <textarea
                id={question.id}
                name={question.id}
                className={styles.textarea}
                rows={5}
                maxLength={question.maxLength}
                placeholder={question.placeholder}
                value={(answers[question.id] as string | undefined) ?? ''}
                disabled={isSubmitting}
                aria-invalid={error ? true : undefined}
                aria-describedby={describedBy}
                onChange={(e) => handleTextChange(question.id, e.target.value)}
              />
            )}

            {question.type === 'text' && question.maxLength != null && (
              <p className={styles.charCount}>
                {((answers[question.id] as string | undefined) ?? '').length} /{' '}
                {question.maxLength}
              </p>
            )}

            {error && (
              <p id={errorId} className={styles.fieldError} role="alert">
                {error}
              </p>
            )}
          </fieldset>
        )
      })}

      <button type="submit" className={styles.submit} disabled={isSubmitting}>
        {isSubmitting ? 'Submitting…' : submitLabel}
      </button>
    </form>
  )
}
