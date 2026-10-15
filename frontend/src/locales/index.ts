import { en } from './en'
import { pl, type Dictionary } from './pl'

// Adding a language: copy en.ts, translate, register it here (+ an e-mail template in the backend, Invitations.cs).
export const dictionaries: Record<string, Dictionary> = { pl, en }
export const defaultLanguage = 'pl'

export type { Dictionary }
