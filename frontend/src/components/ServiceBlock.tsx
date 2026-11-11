import { Link } from 'react-router-dom'
import styles from './ServiceBlock.module.css'

// ---------------------------------------------------------------------------
// ServiceBlock - a single service category card (Requirements 3.1-3.5).
// ---------------------------------------------------------------------------

/** A single service category returned by GET /api/profile/services. */
export interface ServiceBlockData {
  id: string
  category: string
  title: string
  description: string
  offerings: string[]
}

export interface ServiceBlockProps {
  /** The service block to render. */
  block: ServiceBlockData
  /** Optional in-app route; when set, a "Learn more" link is shown. */
  href?: string
  /** Optional label for the link (defaults to "Learn more"). */
  linkLabel?: string
}

/**
 * Renders a single Service Block: title, description, its offerings as a list,
 * and (when an href is supplied) a link through to the relevant detail or
 * survey page.
 */
export default function ServiceBlock({ block, href, linkLabel = 'Learn more' }: ServiceBlockProps) {
  const offerings = Array.isArray(block.offerings) ? block.offerings : []
  const hasOfferings = offerings.length > 0

  return (
    <article className={styles.block} aria-label={block.title}>
      <h2 className={styles.title}>{block.title}</h2>
      <p className={styles.description}>{block.description}</p>

      {hasOfferings && (
        <ul className={styles.offerings}>
          {offerings.map((offering, index) => (
            <li key={`${block.id}-${index}`} className={styles.offering}>
              {offering}
            </li>
          ))}
        </ul>
      )}

      {href && (
        <Link className={styles.link} to={href} aria-label={`${linkLabel}: ${block.title}`}>
          {linkLabel} &#8594;
        </Link>
      )}
    </article>
  )
}
