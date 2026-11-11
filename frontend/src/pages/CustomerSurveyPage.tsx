import SurveyForm, {
  type SurveyAnswers,
  type SurveyQuestion,
} from '../components/SurveyForm'
import { useForm } from '../hooks/useForm'
import styles from './CustomerSurveyPage.module.css'

// ---------------------------------------------------------------------------
// CustomerSurveyPage (Requirements 6.1, 6.2, 6.5, 6.6)
// ---------------------------------------------------------------------------
//
// Renders the `/surveys/customer` route — a Customer Survey reachable from the
// "Surveys" navigation (Requirement 6.1). It composes the config-driven
// {@link SurveyForm} with three questions (Requirement 6.2):
//
//   - serviceSatisfaction — rating on a 1–5 scale (required).
//   - npsScore            — likelihood to recommend on a 0–10 scale (NPS,
//                           required).
//   - openFeedback        — open-ended text up to 1000 characters (optional;
//                           only the two scores are strictly required to
//                           submit, while the API still accepts the feedback).
//
// SurveyForm owns the answer state and required-question validation. This page
// owns only the API wiring via {@link useForm}:
//
//   - On a successful (HTTP 202) acknowledgement the form is replaced with a
//     thank-you confirmation message (Requirement 6.5).
//   - On an error or timeout an error banner is shown and the form stays
//     mounted so the visitor's answers are retained for resubmission
//     (Requirement 6.6). Because SurveyForm owns the answer state, keeping it
//     mounted is exactly what preserves the entered responses.

/** API path for customer survey submissions (design: POST /api/surveys/customer). */
const CUSTOMER_SURVEY_ENDPOINT = '/api/surveys/customer'

/** Maximum length of the open-ended feedback field (Requirement 6.2). */
const FEEDBACK_MAX_LENGTH = 1000

/** Page heading and introductory copy. */
const PAGE_HEADING = 'Customer Survey'
const PAGE_INTRO =
  'We value your feedback. Please take a moment to tell us about your ' +
  'experience with NNM Advisory so we can continue to improve our services.'

/** Success response shape from POST /api/surveys/customer (202 Accepted). */
interface SurveySuccessResponse {
  message?: string
}

/** Default confirmation copy used if the server omits a message. */
const DEFAULT_SUCCESS_MESSAGE =
  'Thank you for completing our customer survey. Your feedback has been received.'

/**
 * The three Customer Survey questions (Requirement 6.2). The two rating scales
 * are required; the open-ended feedback is optional.
 */
const QUESTIONS: SurveyQuestion[] = [
  {
    id: 'serviceSatisfaction',
    label: 'How satisfied are you with our service?',
    type: 'rating',
    required: true,
    min: 1,
    max: 5,
    minLabel: 'Very dissatisfied',
    maxLabel: 'Very satisfied',
  },
  {
    id: 'npsScore',
    label: 'How likely are you to recommend NNM Advisory to a friend or colleague?',
    type: 'rating',
    required: true,
    min: 0,
    max: 10,
    minLabel: 'Not at all likely',
    maxLabel: 'Extremely likely',
  },
  {
    id: 'openFeedback',
    label: 'Is there anything else you would like to share?',
    type: 'text',
    required: false,
    maxLength: FEEDBACK_MAX_LENGTH,
    placeholder: 'Your feedback (optional)',
  },
]

/**
 * Map the SurveyForm answer map to the request body expected by
 * POST /api/surveys/customer. The two ratings are stored as numbers by
 * SurveyForm; the optional feedback is trimmed and omitted (sent as undefined)
 * when empty.
 */
function toRequestBody(answers: SurveyAnswers) {
  const feedback = ((answers.openFeedback as string | undefined) ?? '').trim()
  return {
    serviceSatisfaction: answers.serviceSatisfaction as number,
    npsScore: answers.npsScore as number,
    openFeedback: feedback.length > 0 ? feedback : undefined,
  }
}

/**
 * CustomerSurveyPage renders the Customer Survey and wires its submission to
 * the Backend API.
 */
export default function CustomerSurveyPage() {
  const form = useForm<SurveySuccessResponse>()

  async function handleSubmit(answers: SurveyAnswers) {
    // SurveyForm only invokes this once all required questions are answered
    // (Requirement 6.4 — enforced inside SurveyForm), so no client-side
    // validation is needed here.
    await form.submit(CUSTOMER_SURVEY_ENDPOINT, toRequestBody(answers))
  }

  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>

        {form.isSuccess ? (
          // Requirement 6.5: on success, replace the form with a thank-you message.
          <div
            className={styles.successMessage}
            role="status"
            aria-live="polite"
          >
            <h2 className={styles.successHeading}>Thank you</h2>
            <p>{form.response?.data?.message ?? DEFAULT_SUCCESS_MESSAGE}</p>
          </div>
        ) : (
          <section className={styles.formWrapper} aria-label="Customer survey">
            <p className={styles.intro}>{PAGE_INTRO}</p>

            {/*
             * Requirement 6.6: on error/timeout show a message and keep the
             * form mounted so the visitor's answers are retained. SurveyForm
             * owns the answer state, so leaving it mounted preserves input.
             */}
            {(form.isError || form.isTimeout) && (
              <div className={styles.errorBanner} role="alert">
                <strong className={styles.errorBannerHeading}>
                  {form.isTimeout ? 'Request timed out' : 'Submission unsuccessful'}
                </strong>
                <span>
                  {form.errorMessage ??
                    'We could not submit your survey. Your answers have been kept — please try again.'}
                </span>
              </div>
            )}

            <SurveyForm
              questions={QUESTIONS}
              onSubmit={handleSubmit}
              submitLabel="Submit Survey"
              isSubmitting={form.isLoading}
            />
          </section>
        )}
      </div>
    </main>
  )
}
