import styles from './ServiceDetailPage.module.css'

// ---------------------------------------------------------------------------
// AccountingServicesPage - /services/accounting
// ---------------------------------------------------------------------------

interface Offering {
  title: string
  description: string
}

const OFFERINGS: Offering[] = [
  {
    title: 'Bookkeeping',
    description:
      'Accurate, up-to-date books maintained month to month - bank reconciliations, ledgers, accounts payable and receivable - so you always know where you stand.',
  },
  {
    title: 'Tax Services',
    description:
      'Preparation and filing of company and individual tax, provisional tax planning, and VAT returns, kept compliant with current SARS requirements.',
  },
  {
    title: 'Financial Analysis & Insights',
    description:
      'Management accounts, cash-flow forecasts, and plain-language insight into the numbers, turning your financial data into decisions you can act on.',
  },
]

export default function AccountingServicesPage() {
  return (
    <main>
      <header className={`${styles.hero} ${styles.heroAccounting}`}>
        <div className={styles.heroInner}>
          <p className={styles.eyebrow}>Accounting Services</p>
          <h1 className={styles.title}>Clarity and confidence in your numbers.</h1>
          <p className={styles.lede}>
            Dependable accounting that keeps you compliant, organised, and ready
            for every decision, backed by qualified Chartered Accountant expertise.
          </p>
        </div>
      </header>

      <section className={styles.section} aria-label="Accounting offerings">
        <div className={styles.container}>
          <h2 className={styles.sectionHeading}>What we offer</h2>
          <div className={styles.grid}>
            {OFFERINGS.map((o) => (
              <article key={o.title} className={styles.card}>
                <h3 className={styles.cardTitle}>{o.title}</h3>
                <p className={styles.cardBody}>{o.description}</p>
              </article>
            ))}
          </div>

          <div className={styles.cta}>
            <p className={styles.ctaText}>
              Ready to tidy up your finances? Let's talk about what your business needs.
            </p>
            <a className={styles.ctaLink} href="/contact">Contact us &#8594;</a>
          </div>
        </div>
      </section>
    </main>
  )
}
