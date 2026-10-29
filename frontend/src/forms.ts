import { useQuery } from '@tanstack/react-query'
import { api, basePath } from './api'

export type FieldType = 'Text' | 'Textarea' | 'Email' | 'Phone' | 'SingleChoice' | 'MultiChoice' | 'Sections'

export type FormField = { id: string; type: FieldType; label: string; required: boolean; options: string[] }

export type Form = {
  id: number
  title: string
  description: string | null
  isOpen: boolean
  fields: FormField[]
  createdAt: string
  responses: number
}

// Answers keyed by field id: one value for text fields, the chosen options, or section ids for Sections.
export type Answers = Record<string, string[]>

export const fieldTypes: { value: FieldType; label: string }[] = [
  { value: 'Text', label: 'Short text' },
  { value: 'Textarea', label: 'Long text' },
  { value: 'Email', label: 'E-mail' },
  { value: 'Phone', label: 'Phone' },
  { value: 'SingleChoice', label: 'Single choice' },
  { value: 'MultiChoice', label: 'Multiple choice' },
  { value: 'Sections', label: 'Sections' },
]

export const hasOptions = (type: FieldType) => type === 'SingleChoice' || type === 'MultiChoice'

export const formsKey = ['forms']

export const useForms = () => useQuery({ queryKey: formsKey, queryFn: () => api<Form[]>('/api/forms') })

export const useForm = (id: number | null) =>
  useQuery({ queryKey: [...formsKey, id], queryFn: () => api<Form>(`/api/forms/${id}`), enabled: id !== null })

export const publicFormUrl = (id: number) => `${location.origin}${basePath}/form/${id}`
