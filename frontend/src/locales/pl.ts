// Texts recruits see. Default language. Other languages must have the same keys (type Dictionary).
export const pl = {
  name: 'Polski',
  locale: 'pl-PL', // dates and times

  recruitment: 'Rekrutacja',
  title: 'Zarezerwuj rozmowę rekrutacyjną',
  stepDay: 'Wybierz dzień',
  stepTime: 'Wybierz godzinę',
  stepDetails: 'Podaj swoje dane',
  bookedNote: 'Zapisaliśmy Twój termin. Do zobaczenia!',
  linkInvalid: 'Ten link jest nieprawidłowy',
  linkExpired: 'Ten link wygasł',
  linkProblemHelp: 'Skontaktuj się z osobą, która go wysłała.',
  minutes: (n: number) => `${n} min`,

  booked: 'Twoja rozmowa jest zarezerwowana',
  needChange: 'Chcesz coś zmienić? Napisz do nas:',

  pickTime: 'Wybierz dogodny termin. Godziny są podane w Twojej strefie czasowej.',
  noTimes: 'W tej chwili nie ma wolnych terminów.',
  noGreenTimes: 'W tej chwili nie ma pasujących terminów.',
  noneSuit: 'Żaden z tych terminów mi nie pasuje',
  yellowExplained: 'Terminy oznaczone na żółto też są możliwe, ale jest wtedy dostępnych mniej osób – wybierz je tylko, jeśli żaden zielony Ci nie pasuje.',
  contactIfNone: 'Jeśli nadal nic Ci nie pasuje, napisz do nas:',

  fullName: 'Imię i nazwisko',
  email: 'E-mail',
  discord: 'Nick na Discordzie',
  discordHelp: 'Opcjonalnie, ale zalecane – kontaktujemy się z członkami przez Discorda.',
  discordPlaceholder: 'np. jan_kowalski',
  phone: 'Telefon (opcjonalnie)',
  consent: 'Wyrażam zgodę na przetwarzanie moich danych osobowych w celu przeprowadzenia tej rekrutacji.',
  privacyLink: 'Jak wykorzystujemy Twoje dane',
  privacy: (contact: string | null) =>
    `Twoje imię i nazwisko, e-mail, telefon i nick na Discordzie wykorzystujemy wyłącznie do zorganizowania rozmowy i podjęcia decyzji rekrutacyjnej. ` +
    `Widzi je tylko zespół rekrutacyjny. Usuwamy je po zakończeniu rekrutacji. W każdej chwili możesz poprosić o wgląd w swoje dane, ich poprawienie lub usunięcie` +
    `${contact ? `, pisząc na ${contact}` : ''}. Zgoda jest dobrowolna, ale bez niej nie możemy umówić rozmowy.`,
  book: 'Rezerwuję ten termin',

  taken: 'Ktoś właśnie zajął ten termin albo nie jest już dostępny. Wybierz inny.',
  alreadyBooked: 'Rozmowa została już zarezerwowana tym linkiem.',
  invalidName: 'Podaj imię i nazwisko.',
  invalidEmail: 'Podaj poprawny adres e-mail.',
  invalidDiscord: 'Nick jest za długi (maks. 50 znaków).',
  invalidPhone: 'Numer jest za długi.',
  somethingWrong: 'Coś poszło nie tak. Spróbuj ponownie.',
}

export type Dictionary = typeof pl
