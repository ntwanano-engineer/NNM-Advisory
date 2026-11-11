import { useState } from 'react'
import SurveyForm, {
  type SurveyAnswers,
  type SurveyQuestion,
} from '../components/SurveyForm'
import { apiPost } from '../apiClient'
import styles from './EmployeeSurveyPage.module.css'

// ---------------------------------------------------------------------------
// EmployeeSurveyPage (Requirements 7.1, 7.2, 7.5, 7.6, 7.7, 7.8)
// ---------------------------------------------------------------------------
//
// Renders the `/surveys/employee` route, accessible from the Surveys
// navigation (Requirement 7.1). The survey is driven by the shared, config-
// driven {@link SurveyForm}, which owns answer state, renders the required
// markers before any submit attempt (Requirement 7.8), and performs the
// client-side required-question validation (Requirement 7.4) — it only invokes
// our `onSubmit` once every required question is answered.
//
// Question categories (Requirement 7.2 — ≥ 1 question per category):
//   - employee engagement   → `engagementScore` (1–5 rating, required)
//   - workplace satisfaction → `workplaceSatisfaction` (text, required)
//   - open-ended feedback    → `openFeedback` (text, required)
//
// The survey collects NO personally identifiable information — there are no
// name, email, or employee-id fields (Requirement 7.6).

/** API path for employee survey submissions (design: POST /api/surveys/employee). */
const EMPLOYEE_SURVEY_ENDPOINT = '/api/surveys/employee'

/** Question ids — also used as the keys in the collected answer map. */
const QUESTION_IDS = {
  engagementScore: 'engagementScore',
  workplaceSatisfaction: 'workplaceSatisfaction',
  openFeedback: 'openFeedback',
} as const

/** Maximum length for the open-ended feedback answer. */
const OPEN_FEEDBACK_MAX_LENGTH = 1000

/** Maximum length for the workplace-satisfaction text answer. */
const WORKPLACE_SATISFACTION_MAX_LENGTH = 1000

/**
 * The survey questions, one per required category (Requirement 7.2). All three
 * are marked `required: true` so SurveyForm renders the required indicator up
 * front (Requirement 7.8) and blocks submission until each is answered
 * (Requirement 7.4).
 */
const QUESTIONS: SurveyQuestion[] = [
  {
    id: QUESTION_IDS.engagementScore,
    label: 'How engaged do you feel in your work?',
    type: 'rating',
    required: true,
    min: 1,
    max: 5,
    minLabel: 'Not engaged',
    maxLabel: 'Fully engaged',
    description:
      'Rate your overall sense of engagement and motivation at work.',
  },
  {
    id: QUESTION_IDS.workplaceSatisfaction,
    label: 'How satisfied are you with your workplace?',
    type: 'text',
    required: true,
    maxLength: WORKPLACE_SATISFACTION_MAX_LENGTH,
    placeholder:
      'Share what is working well and what could improve about your workplace.',
    description:
      'Tell us about your satisfaction with your working environment, tools, and support.',
  },
  {
    id: QUESTION_IDS.openFeedback,
    label: 'Is there anything else you would like to share?',
    type: 'text',
    required: true,
    maxLength: OPEN_FEEDBACK_MAX_LENGTH,
    placeholder: 'Your open-ended feedback…',
    description:
      'Any additional thoughts, suggestions, or concerns you would like leadership to hear.',
  },
]

/** Request payload for POST /api/surveys/employee (no PII — Requirement 7.6). */
interface EmployeeSurveyPayload {
  engagementScore: number
  workplaceSatisfaction: string
  openFeedback: string
}

/** Page heading and introductory copy. */
const PAGE_HEADING = 'Employee Survey'
const PAGE_INTRO =
  'Your feedback helps us understand how to improve the workplace. This survey ' +
  'is anonymous — we do not collect your name, email, or any identifying details.'

/** Discrete states for the submission lifecycle on this page. */
type SubmitState =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'success' }
  | { kind: 'error'; message: string }

/** Confirmation copy shown on a successful submission. */
const THANK_YOU_HEADING = 'Thank you'
const THANK_YOU_MESSAGE =
  'Your survey has been submitted successfully. We appreciate you taking the ' +
  'time to share your feedback.'

/**
 * Map the SurveyForm answer map to the API payload.
 *
 * SurveyForm stores rating answers as numbers and text answers as strings, so
 * the ratings coerce cleanly and the text answers are trimmed before sending.
 */
function toPayload(answers: SurveyAnswers): EmployeeSurveyPayload {
  return {
    engagementScore: Number(answers[QUESTION_IDS.engagementScore]),
    workplaceSatisfaction: String(
      answers[QUESTION_IDS.workplaceSatisfaction] ?? '',
    ).trim(),
    openFeedback: String(answers[QUESTION_IDS.openFeedback] ?? '').trim(),
  }
}

/**
 * EmployeeSurveyPage renders the employee survey and wires {@link SurveyForm}
 * to the backend.
 *
 * Behaviour:
 * - On success (HTTP 202), the form is replaced with a thank-you confirmation
 *   that remains visible — the page performs NO automatic navigation, so the
 *   message stays on screen well beyond the 5-second minimum (Requirement 7.5).
 * - On an error or timeout, an error banner is shown AND the form stays mounted
 *   so the respondent's answers are retained for resubmission (Requirement 7.7).
 */
export default function EmployeeSurveyPage() {
  const [state, setState] = useState<SubmitState>({ kind: 'idle' })

  async function handleSubmit(answers: SurveyAnswers): Promise<void> {
    setState({ kind: 'submitting' })

    const response = await apiPost(EMPLOYEE_SURVEY_ENDPOINT, toPayload(answers))

    if (response.ok) {
      // Requirement 7.5: show a thank-you confirmation. We do not auto-navigate,
      // so it remains visible indefinitely (comfortably past the 5s minimum).
      setState({ kind: 'success' })
      return
    }

    // Requirement 7.7: on failure, surface an error and keep the form mounted so
    // the already-entered answers are retained for a retry.
    setState({
      kind: 'error',
      message:
        response.errorMessage ??
        'We could not submit your survey. Please try again.',
    })
  }

  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>

        {state.kind === 'success' ? (
          // Requirement 7.5: persistent thank-you confirmation (no auto-nav).
          <div
            className={styles.successMessage}
            role="status"
            aria-live="polite"
          >
            <h2 className={styles.successHeading}>{THANK_YOU_HEADING}</h2>
            <p>{THANK_YOU_MESSAGE}</p>
          </div>
        ) : (
          <>
            <p className={styles.intro}>{PAGE_INTRO}</p>

            <section
              className={styles.formWrapper}
              aria-label="Employee survey"
            >
              {state.kind === 'error' && (
                // Requirement 7.7: error banner; the form below retains answers.
                <div className={styles.errorBanner} role="alert">
                  <strong className={styles.errorBannerHeading}>
                    Submission unsuccessful
                  </strong>
                  <span>{state.message}</span>
                </div>
              )}

              <SurveyForm
                questions={QUESTIONS}
                onSubmit={handleSubmit}
                submitLabel="Submit Survey"
                isSubmitting={state.kind === 'submitting'}
              />
            </section>
          </>
        )}
      </div>
    </main>
  )
}
