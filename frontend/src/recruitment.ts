import { useQuery } from '@tanstack/react-query'
import { api } from './api'

// Dates as "YYYY-MM-DD" in the club time zone, both inclusive; null = booking closed.
export type RecruitmentPeriod = { interviewsFrom: string | null; interviewsTo: string | null }

export const recruitmentKey = ['settings', 'recruitment']

export const useRecruitmentPeriod = () =>
  useQuery({ queryKey: recruitmentKey, queryFn: () => api<RecruitmentPeriod>('/api/settings/recruitment') })

const localDate = (ymd: string) => new Date(`${ymd}T00:00:00`)

export const formatDay = (ymd: string) => localDate(ymd).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'long', year: 'numeric' })

// [start of first day, start of the day after the last day) – for FullCalendar's validRange.
export const periodRange = (p: RecruitmentPeriod) => {
  if (!p.interviewsFrom || !p.interviewsTo) return null
  const end = localDate(p.interviewsTo)
  end.setDate(end.getDate() + 1)
  return { start: localDate(p.interviewsFrom), end }
}
