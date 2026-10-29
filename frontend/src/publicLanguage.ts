import { useEffect, useState } from 'react'
import { defaultLanguage, dictionaries, type Dictionary } from './locales'

// The visitor's own choice is remembered in this browser; storage may be unavailable (private mode) – then just don't remember.
const LANGUAGE_KEY = 'rekrutime.booking.language'
const storedLanguage = () => {
  try {
    return localStorage.getItem(LANGUAGE_KEY)
  } catch {
    return null
  }
}

// Language of a public page: the visitor's choice, else the one the server suggests (e.g. the link's), else the default.
export function usePublicLanguage(suggested: string | undefined, title: (t: Dictionary) => string) {
  const [chosen, setChosen] = useState(storedLanguage)
  const language = [chosen, suggested, defaultLanguage].find((l) => l && dictionaries[l])!
  const t = dictionaries[language]

  useEffect(() => {
    document.documentElement.lang = language
    document.title = title(t)
  }, [language, t, title])

  const choose = (l: string) => {
    setChosen(l)
    try {
      localStorage.setItem(LANGUAGE_KEY, l)
    } catch {
      /* not remembered – fine */
    }
  }
  return { language, t, choose }
}
