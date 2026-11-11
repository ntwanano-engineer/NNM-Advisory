import { Link } from 'react-router-dom'

/**
 * NotFoundPage — renders for the `*` catch-all route.
 *
 * Requirement 1.5: IF a Visitor navigates to a URL path that does not exist,
 * THE Website SHALL display a 404 Not Found page with a link back to the Home page.
 */
export default function NotFoundPage() {
  return (
    <main>
      <h1>404 — Page Not Found</h1>
      <p>The page you are looking for does not exist.</p>
      <Link to="/">Back to Home</Link>
    </main>
  )
}
