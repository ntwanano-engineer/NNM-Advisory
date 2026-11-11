import { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route, useLocation } from 'react-router-dom'
import { AppProvider } from './context/AppContext'
import ProtectedRoute from './components/ProtectedRoute'
import Navbar from './components/Navbar'
import Footer from './components/Footer'

// ---------------------------------------------------------------------------
// Lazy-loaded page components (Requirements 1.1, 1.2)
// Each page is code-split into its own chunk to minimise initial bundle size.
// ---------------------------------------------------------------------------
const HomePage            = lazy(() => import('./pages/HomePage'))
const ServicesPage        = lazy(() => import('./pages/ServicesPage'))
const AccountingServicesPage = lazy(() => import('./pages/AccountingServicesPage'))
const ITServicesPage      = lazy(() => import('./pages/ITServicesPage'))
const MissionPage         = lazy(() => import('./pages/MissionPage'))
const AboutPage           = lazy(() => import('./pages/AboutPage'))
const SurveysPage         = lazy(() => import('./pages/SurveysPage'))
const CustomerSurveyPage  = lazy(() => import('./pages/CustomerSurveyPage'))
const EmployeeSurveyPage  = lazy(() => import('./pages/EmployeeSurveyPage'))
const ContactPage         = lazy(() => import('./pages/ContactPage'))
const AdminLoginPage      = lazy(() => import('./pages/AdminLoginPage'))
const AdminDashboardPage  = lazy(() => import('./pages/AdminDashboardPage'))
const NotFoundPage        = lazy(() => import('./pages/NotFoundPage'))

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------

/**
 * App is the root component.  It:
 * - Wraps everything in AppProvider (global auth + content-cache state)
 * - Configures BrowserRouter with the full route table from the design doc
 * - Wraps all lazy-loaded pages in a single Suspense boundary
 * - Guards /admin behind ProtectedRoute (Requirement 8.1, 8.2)
 */
/**
 * AppLayout renders the persistent Navbar and Footer around the routed page
 * content. The public chrome (nav + footer) is hidden on the admin screens
 * (/admin and /admin/login), which are standalone app views.
 */
function AppLayout() {
  const location = useLocation()
  const isAdmin = location.pathname.startsWith('/admin')

  return (
    <>
      {!isAdmin && <Navbar />}
      <Suspense fallback={<div aria-live="polite">Loading…</div>}>
        <Routes>
          {/* Public routes */}
          <Route path="/"                  element={<HomePage />} />
          <Route path="/services"          element={<ServicesPage />} />
          <Route path="/services/accounting" element={<AccountingServicesPage />} />
          <Route path="/services/it"       element={<ITServicesPage />} />
          <Route path="/mission"           element={<MissionPage />} />
          <Route path="/about"             element={<AboutPage />} />
          <Route path="/surveys"           element={<SurveysPage />} />
          <Route path="/surveys/customer"  element={<CustomerSurveyPage />} />
          <Route path="/surveys/employee"  element={<EmployeeSurveyPage />} />
          <Route path="/contact"           element={<ContactPage />} />
          <Route path="/admin/login"       element={<AdminLoginPage />} />

          {/* Protected admin route - requires valid JWT (Requirements 8.1, 8.2) */}
          <Route
            path="/admin"
            element={
              <ProtectedRoute>
                <AdminDashboardPage />
              </ProtectedRoute>
            }
          />

          {/* 404 catch-all - link back to Home (Requirement 1.5) */}
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </Suspense>
      {!isAdmin && <Footer />}
    </>
  )
}

function App() {
  return (
    <AppProvider>
      <BrowserRouter
        future={{
          v7_startTransition: true,
          v7_relativeSplatPath: true,
        }}
      >
        <AppLayout />
      </BrowserRouter>
    </AppProvider>
  )
}

export default App
