import { describe, it, expect, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import fc from 'fast-check'
import SurveyForm, { type SurveyQuestion } from './SurveyForm'

// ---------------------------------------------------------------------------
// Property 6 (client-side): Survey Incomplete Submission
// Validates: Requirements 6.4, 7.4
//
// For any arbitrary, non-empty subset of required questions left UNANSWERED,
// SurveyForm must:
//   - NOT call its `onSubmit` callback (so no API request is ever made), and
//   - display an inline error (role="alert") for every unanswered required
//     question, and for NONE of the answered ones.
//
// Strategy: a small fixed set of three required questions (a mix of rating and
// text). fast-check picks a non-empty subset of their indices to leave blank;
// the remaining questions are answered before submit is clicked.
// ---------------------------------------------------------------------------

// A small, fixed question set keeps per-run render cost low (3 questions).
// All are required so every omission is a validation failure.
const QUESTIONS: SurveyQuestion[] = [
  {
    id: 'satisfaction',
    label: 'How satisfied are you with our service?',
    type: 'rating',
    required: true,
    min: 1,
    max: 5,
  },
  {
    id: 'nps',
    label: 'How likely are you to recommend us?',
    type: 'rating',
    required: true,
    min: 0,
    max: 10,
  },
  {
    id: 'feedback',
    label: 'Any additional feedback?',
    type: 'text',
    required: true,
    maxLength: 1000,
  },
]

const errorId = (id: string) => `${id}-error`

/** Answer a single question in the rendered form. */
function answerQuestion(question: SurveyQuestion) {
  if (question.type === 'rating') {
    // Select the lowest value in the scale via its radio input.
    const radio = document.getElementById(
      `${question.id}-${question.min}`
    ) as HTMLInputElement | null
    expect(radio).not.toBeNull()
    fireEvent.click(radio as HTMLInputElement)
  } else {
    const textarea = document.getElementById(
      question.id
    ) as HTMLTextAreaElement | null
    expect(textarea).not.toBeNull()
    fireEvent.change(textarea as HTMLTextAreaElement, {
      target: { value: 'A valid answer.' },
    })
  }
}

describe('SurveyForm — Property 6 (client-side): Survey Incomplete Submission', () => {
  it('never calls onSubmit and shows an inline error for each unanswered required question', async () => {
    await fc.assert(
      fc.asyncProperty(
        // A non-empty subset of question indices to leave UNANSWERED.
        fc
          .subarray(
            QUESTIONS.map((_, i) => i),
            { minLength: 1, maxLength: QUESTIONS.length }
          ),
        async (missingIndices) => {
          const missing = new Set(missingIndices)
          const onSubmit = vi.fn()

          render(<SurveyForm questions={QUESTIONS} onSubmit={onSubmit} />)

          // Answer every question NOT in the missing set.
          QUESTIONS.forEach((question, index) => {
            if (!missing.has(index)) {
              answerQuestion(question)
            }
          })

          // Attempt submission.
          fireEvent.click(
            screen.getByRole('button', { name: /submit survey/i })
          )

          // Requirements 6.4 / 7.4: with required questions unanswered the
          // form must not invoke the submit callback (no API call).
          expect(onSubmit).not.toHaveBeenCalled()

          // Every unanswered required question shows an inline error alert;
          // every answered question does not.
          QUESTIONS.forEach((question, index) => {
            const el = document.getElementById(errorId(question.id))
            if (missing.has(index)) {
              expect(el).not.toBeNull()
              expect(el).toHaveAttribute('role', 'alert')
            } else {
              expect(el).toBeNull()
            }
          })

          // There must be exactly as many alerts as unanswered questions.
          expect(screen.queryAllByRole('alert')).toHaveLength(missing.size)

          cleanup()
        }
      ),
      { numRuns: 100 }
    )
  })
})
