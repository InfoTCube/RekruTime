import type { Dictionary } from './pl'

export const en: Dictionary = {
  name: 'English',
  locale: 'en-GB',

  recruitment: 'Recruitment',
  title: 'Book your recruitment interview',
  stepDay: 'Choose a day',
  stepTime: 'Choose a time',
  stepDetails: 'Your details',
  bookedNote: "We've saved your time. See you there!",
  linkInvalid: 'This link is not valid',
  linkExpired: 'This link has expired',
  linkProblemHelp: 'Please contact the person who sent it to you.',
  minutes: (n: number) => `${n} min`,

  booked: 'Your interview is booked',
  needChange: 'Need to change it? Write to us at',

  pickTime: 'Pick a time that suits you. Times are shown in your local time.',
  noTimes: 'There are no free times at the moment.',
  noGreenTimes: 'There are no free times that fit at the moment.',
  noneSuit: 'None of these times suit me',
  yellowExplained: 'Times marked in yellow are also possible, but fewer of us are available then – please use them only if no green time works.',
  contactIfNone: 'If none of these times suit you either, contact us at',

  fullName: 'Full name',
  email: 'E-mail',
  discord: 'Discord nickname',
  discordHelp: 'Optional, but recommended – we talk to members on Discord.',
  discordPlaceholder: 'e.g. jan_kowalski',
  phone: 'Phone (optional)',
  consent: 'I agree to the processing of my personal data for the purpose of this recruitment.',
  privacyLink: 'How we use your data',
  privacy: (contact: string | null) =>
    `We use your name, e-mail, phone and Discord nickname only to organise your interview and the recruitment decision. ` +
    `Only the recruitment team can see them. We delete them after the recruitment ends. You can ask us to see, correct or delete your data at any time` +
    `${contact ? ` by writing to ${contact}` : ''}. Agreeing is voluntary, but we can't book an interview without it.`,
  book: 'Book this time',

  taken: 'Someone has just taken this time, or it is no longer available. Please pick another one.',
  alreadyBooked: 'An interview has already been booked with this link.',
  invalidName: 'Please enter your full name.',
  invalidEmail: 'Please enter a valid e-mail address.',
  invalidDiscord: 'The nickname is too long (max 50 characters).',
  invalidPhone: 'The number is too long.',
  somethingWrong: 'Something went wrong. Please try again.',
  // Recruitment form (/form/:id)
  formPageTitle: 'Recruitment form',
  formNotFound: "This form doesn't exist",
  formClosed: 'This form is closed',
  formProblemHelp: 'If you have questions, write to us at',
  submit: 'Send application',
  editAnswers: 'Change my answers',
  submitted: 'Thank you! Your application has been sent.',
  submittedNote: "We'll e-mail you about the next steps. To change anything, just send the form again – we'll keep your new answers.",
  requiredAnswer: 'Please answer this question.',
  invalidAnswer: 'Please check this answer.',
  formPrivacy: (contact: string | null) =>
    `We use your name, e-mail and answers only for this recruitment. ` +
    `Only the recruitment team can see them. We delete them after the recruitment ends. You can ask us to see, correct or delete your data at any time` +
    `${contact ? ` by writing to ${contact}` : ''}. Agreeing is voluntary, but we can't accept your application without it.`,
}
