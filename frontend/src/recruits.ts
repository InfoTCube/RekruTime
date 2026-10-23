import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import { isAdmin, type Me } from './auth'
import type { Interview, InterviewStatus, Notes } from './interviews'

export type ApplicationStatus = 'Pending' | 'Disputed' | 'Accepted' | 'Rejected'
export type Decision = 'Approve' | 'Reject'

export type Vote = { userId: string; name: string; decision: Decision; isFinal: boolean; comment: string | null; createdAt: string }
// canVote / canDecide (final decision on a disputed application) are worked out by the API for the current user.
export type Application = { id: number; sectionId: number; sectionName: string; status: ApplicationStatus; votes: Vote[]; canVote: boolean; canDecide: boolean }

export type RecruitRow = {
  id: number
  fullName: string
  email: string
  applications: Application[]
  interview: { id: number; start: string; status: InterviewStatus; interviewers: string[] } | null
}

export type RecruitDetail = {
  recruit: Interview['recruit']
  applications: Application[]
  interviews: { interview: Interview; notes: Notes }[]
}

export const recruitsKey = ['recruits']

export const useRecruits = (params: Record<string, string | undefined>) => {
  const search = new URLSearchParams(Object.entries(params).filter((e): e is [string, string] => !!e[1]))
  return useQuery({ queryKey: [...recruitsKey, params], queryFn: () => api<RecruitRow[]>(`/api/recruits?${search}`) })
}

export const statusColor: Record<ApplicationStatus, string> = { Pending: 'gray', Disputed: 'spark', Accepted: 'signal', Rejected: 'red' }

// Admins and section leaders review recruits.
export const isReviewer = (me: Me | null | undefined) => isAdmin(me) || !!me?.sections.some((s) => s.isLeader)
