import { Link } from 'react-router-dom'
import styles from './SurveysPage.module.css'

// ---------------------------------------------------------------------------
// SurveysPage
// ---------------------------------------------------------------------------

/** Page heading and introductory copy. */
const PAGE_HEADING = 'Surveys'
const PAGE_INTRO =
  'Share your feedback with NNM Advisory. Choose the survey that fits you ' +
  'below — your responses help us improve the experience for everyone.'

/** The two survey options rendered as cards linking to their sub-routes. */
interface SurveyOption {
  to: string
  title: string
  description: string
  cta: string
}

const SURVEY_OPTIONS: SurveyOption[] = [
  {
    to: '/surveys/customer',
    title: 'Customer Survey',
    description:
      'Tell us about your experience as a customer. Your satisfaction and ' +
      'loyalty feedback helps us serve you better.',
    cta: 'Start customer survey',
  },
  {
    to: '/surveys/employee',
    title: 'Employee Survey',
    description:
      'An anonymous survey for team members to share engagement and workplace ' +
      'sentiment, helping us build a stronger team.',
    cta: 'Start employee survey',
  },
]

/**
 * SurveysPage renders the `/surveys` landing route (Requirements 6.1, 7.1).
 *
 * It introduces the available surveys and links to the two sub-routes —
 * `/surveys/customer` and `/surveys/employee` — as clear, keyboard-accessible
 * cards. Styling mirrors {@link ServicesPage} for a consistent look.
 */
export default function SurveysPage() {
  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>
        <p className={styles.intro}>{PAGE_INTRO}</p>

        <section className={styles.grid} aria-label="Available surveys">
          {SURVEY_OPTIONS.map((option) => (
            <Link key={option.to} to={option.to} className={styles.card}>
              <h2 className={styles.cardTitle}>{option.title}</h2>
              <p className={styles.cardDescription}>{option.description}</p>
              <span className={styles.cardCta} aria-hidden="true">
                {option.cta} &rarr;
              </span>
            </Link>
          ))}
        </section>
      </div>
    </main>
  )
}
