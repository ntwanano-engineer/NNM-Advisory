import ContactForm from '../components/ContactForm'
import styles from './ContactPage.module.css'

// ---------------------------------------------------------------------------
// ContactPage (Requirement 5.1)
// ---------------------------------------------------------------------------

/** Page heading and introductory copy for the Contact section. */
const PAGE_HEADING = 'Contact Us'
const PAGE_INTRO =
  'Have a question or want to work with us? Send a message using the form ' +
  'below and a member of the NNM Advisory team will be in touch shortly.'

/**
 * ContactPage renders the `/contact` route (Requirement 5.1).
 *
 * It presents a Contact section with a heading and short intro, then renders
 * the {@link ContactForm}, which owns all field state, inline validation, and
 * submission handling.
 */
export default function ContactPage() {
  return (
    <main className={styles.page}>
      <div className={styles.container}>
        <h1 className={styles.heading}>{PAGE_HEADING}</h1>
        <p className={styles.intro}>{PAGE_INTRO}</p>

        <section className={styles.formWrapper} aria-label="Contact form">
          <ContactForm />
        </section>
      </div>
    </main>
  )
}
