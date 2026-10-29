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

export type Note = { authorId: string | null; authorName: string; text: string; updatedAt: string }
export type Notes = { canWrite: boolean; notes: Note[] }
// Interview prep (#93): the recruit's form answers and the sections' suggested questions.
export type Prep = {
  forms: { formId: number; title: string; submittedAt: string; answers: { question: string; values: string[]; followUp: string | null }[] }[]
  sections: { id: number; name: string; questions: string[] }[]
}

export const interviewsKey = ['interviews']
export const notesKey = (id: number) => [...interviewsKey, id, 'notes']

export const useInterviews = (params: Record<string, string | undefined>, enabled = true) => {
  const search = new URLSearchParams(Object.entries(params).filter((e): e is [string, string] => !!e[1]))
  return useQuery({
    queryKey: [...interviewsKey, params],
    queryFn: () => api<Interview[]>(`/api/interviews?${search}`),
    enabled,
  })
}

export const statusColor: Record<InterviewStatus, string> = { Scheduled: 'cortex', Cancelled: 'red', Done: 'gray' }
