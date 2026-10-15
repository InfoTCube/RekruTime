import { useQuery } from '@tanstack/react-query'
import { api } from './api'

export type InterviewStatus = 'Scheduled' | 'Cancelled' | 'Done'

export type Interview = {
  id: number
  start: string
  end: string
  blockedUntil: string
  status: InterviewStatus
  recruit: { id: number; fullName: string; email: string; phone: string | null; discordNickname: string | null }
  sections: { id: number; name: string }[]
  interviewers: { id: string; name: string }[]
}

export const interviewsKey = ['interviews']

export const useInterviews = (params: Record<string, string | undefined>, enabled = true) => {
  const search = new URLSearchParams(Object.entries(params).filter((e): e is [string, string] => !!e[1]))
  return useQuery({
    queryKey: [...interviewsKey, params],
    queryFn: () => api<Interview[]>(`/api/interviews?${search}`),
    enabled,
  })
}

export const statusColor: Record<InterviewStatus, string> = { Scheduled: 'blue', Cancelled: 'red', Done: 'gray' }
