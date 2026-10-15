// Visual identity – everything brand-specific lives here (+ theme.css, Logo.tsx, public/favicon.svg).
// The HTML e-mail can't use CSS variables: its copies of these colours are in backend …/Email/EmailHtml.cs.
import '@fontsource-variable/bricolage-grotesque'
import '@fontsource-variable/instrument-sans'
import { createTheme, type MantineColorsTuple } from '@mantine/core'
import './theme.css'

// Cortex – deep indigo: ink, primary actions. Signal – teal: the trace, "good" (green) slots, focus.
// Spark – amber: "possible but not ideal" (yellow) slots.
const cortex: MantineColorsTuple = ['#eeeefb', '#dcdbf3', '#b6b4e6', '#8e8bd9', '#6c68cd', '#5753c5', '#4a46b3', '#3b3896', '#2e2b77', '#26235c']
const signal: MantineColorsTuple = ['#e3fbf8', '#c4f3ee', '#8de6dd', '#52d7ca', '#26c6b8', '#13b5a8', '#0e9a8f', '#0c8178', '#086660', '#054c48']
const spark: MantineColorsTuple = ['#fff8e6', '#feefc6', '#fbe08f', '#f8cf58', '#f4b740', '#e9a21c', '#c98410', '#a1660d', '#7a4c0d', '#57360a']
// Dark mode surfaces, tinted towards cortex instead of neutral grey.
const dark: MantineColorsTuple = ['#c9c9de', '#a6a7c4', '#7c7ea3', '#5a5c80', '#3d3f5f', '#2f3150', '#242640', '#1b1d33', '#15162a', '#0f1020']

export const theme = createTheme({
  colors: { cortex, signal, spark, dark },
  primaryColor: 'cortex',
  primaryShade: { light: 7, dark: 4 },
  fontFamily: "'Instrument Sans Variable', system-ui, sans-serif",
  headings: { fontFamily: "'Bricolage Grotesque Variable', system-ui, sans-serif", fontWeight: '650' },
  defaultRadius: 'md',
  cursorType: 'pointer',
  focusRing: 'auto',
})

// Slot colours used everywhere (booking page, coverage preview, calendar).
export const slotColor = { Green: 'signal', Yellow: 'spark' } as const
