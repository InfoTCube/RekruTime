import { useQuery } from '@tanstack/react-query'
import { api } from './api'

export type Section = {
  id: number
  name: string
  interviewDurationMinutes: number
  bufferMinutes: number
}

export const sectionsKey = ['sections']

export const useSections = () => useQuery({ queryKey: sectionsKey, queryFn: () => api<Section[]>('/api/sections') })
