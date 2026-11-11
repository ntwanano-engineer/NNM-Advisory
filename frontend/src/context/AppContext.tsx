import React, { createContext, useContext, useReducer, useCallback } from 'react'
import { useInactivityTimer } from '../hooks/useInactivityTimer'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Shape of a single leadership profile returned from GET /api/profile/leadership */
export interface LeadershipProfile {
  id: string
  fullName: string
  title: string
  biography: string
  photoUrl?: string
  displayOrder: number
}

/** Shape of a single service block returned from GET /api/profile/services */
export interface ServiceBlock {
  id: string
  category: string
  title: string
  description: string
  offerings?: string[]
  displayOrder: number
}

/** Cached company profile content (tagline, hero CTA, etc.) */
export interface CompanyProfile {
  tagline?: string
  heroCtaLabel?: string
  [key: string]: string | undefined
}

/** The content cache keyed by resource type */
export interface ContentCache {
  profile: CompanyProfile | null
  services: ServiceBlock[] | null
  leadership: LeadershipProfile[] | null
}

/** Global application state managed by useReducer */
export interface AppState {
  /** JWT string stored when admin logs in, cleared on logout or inactivity timeout */
  authToken: string | null
  contentCache: ContentCache
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

export type AppAction =
  | { type: 'SET_AUTH_TOKEN'; payload: string }
  | { type: 'CLEAR_AUTH_TOKEN' }
  | { type: 'SET_PROFILE_CACHE'; payload: CompanyProfile }
  | { type: 'SET_SERVICES_CACHE'; payload: ServiceBlock[] }
  | { type: 'SET_LEADERSHIP_CACHE'; payload: LeadershipProfile[] }
  | { type: 'CLEAR_CONTENT_CACHE' }

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

const initialState: AppState = {
  authToken: null,
  contentCache: {
    profile: null,
    services: null,
    leadership: null,
  },
}

function appReducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case 'SET_AUTH_TOKEN':
      return { ...state, authToken: action.payload }

    case 'CLEAR_AUTH_TOKEN':
      return { ...state, authToken: null }

    case 'SET_PROFILE_CACHE':
      return {
        ...state,
        contentCache: { ...state.contentCache, profile: action.payload },
      }

    case 'SET_SERVICES_CACHE':
      return {
        ...state,
        contentCache: { ...state.contentCache, services: action.payload },
      }

    case 'SET_LEADERSHIP_CACHE':
      return {
        ...state,
        contentCache: { ...state.contentCache, leadership: action.payload },
      }

    case 'CLEAR_CONTENT_CACHE':
      return {
        ...state,
        contentCache: { profile: null, services: null, leadership: null },
      }

    default:
      return state
  }
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

interface AppContextValue {
  state: AppState
  dispatch: React.Dispatch<AppAction>
  /** Convenience: set the auth token in both state and localStorage */
  setAuthToken: (token: string) => void
  /** Convenience: clear the auth token from state and localStorage */
  clearAuthToken: () => void
}

const AppContext = createContext<AppContextValue | null>(null)

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

interface AppProviderProps {
  children: React.ReactNode
}

/**
 * AppProvider wraps the application and provides global state for the auth
 * token and the API content cache.  On mount it rehydrates the auth token
 * from localStorage so the admin session survives a page refresh.
 */
export function AppProvider({ children }: AppProviderProps) {
  // Rehydrate token from localStorage on initial render
  const storedToken = localStorage.getItem('nnm_auth_token')
  const [state, dispatch] = useReducer(appReducer, {
    ...initialState,
    authToken: storedToken ?? null,
  })

  const setAuthToken = useCallback((token: string) => {
    localStorage.setItem('nnm_auth_token', token)
    dispatch({ type: 'SET_AUTH_TOKEN', payload: token })
  }, [])

  const clearAuthToken = useCallback(() => {
    localStorage.removeItem('nnm_auth_token')
    dispatch({ type: 'CLEAR_AUTH_TOKEN' })
  }, [])

  // Admin inactivity timeout (Requirement 8.6): while an admin session is
  // active (a token is present), reset a 60-minute countdown on every API
  // activity. When it elapses, clear the JWT so ProtectedRoute redirects on the
  // next render, and navigate to the login page immediately.
  const handleInactivityTimeout = useCallback(() => {
    clearAuthToken()
    if (typeof window !== 'undefined') {
      window.location.assign('/admin/login')
    }
  }, [clearAuthToken])

  useInactivityTimer({
    enabled: state.authToken !== null,
    onTimeout: handleInactivityTimeout,
  })

  const value: AppContextValue = {
    state,
    dispatch,
    setAuthToken,
    clearAuthToken,
  }

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useAppContext returns the global app context.
 * Must be used inside AppProvider.
 */
export function useAppContext(): AppContextValue {
  const ctx = useContext(AppContext)
  if (ctx === null) {
    throw new Error('useAppContext must be used within an AppProvider')
  }
  return ctx
}

