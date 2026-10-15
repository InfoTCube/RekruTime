import { useQuery } from '@tanstack/react-query'
import { api, ApiError } from './api'

export type Me = {
  id: string
  email: string
  displayName: string
  roles: string[]
}

export const meKey = ['me']

// null = not logged in (401), so guards can tell "logged out" apart from "still loading".
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      try {
        return await api<Me>('/api/auth/me')
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: Infinity,
  })
}

export const isAdmin = (me: Me | null | undefined) => !!me?.roles.includes('Admin')
