import { expect, test } from '@playwright/test'
import { club, json, runId } from './helpers'

test('an English link is in English, the language can be switched, and the layout fits a phone', async ({ browser }) => {
  const run = runId()
  const { admin, section } = await club(run)
  const imported = await json(admin.post('/api/booking-links/import', {
    data: { rows: [{ line: 2, name: `Lucas ${run}`, email: `lucas.${run}@recruit.e2e`, sectionIds: [section.id], language: 'en' }], dryRun: false, sendEmails: false },
  }))
  expect(imported.created).toBe(1)
  const link = (await json(admin.get('/api/booking-links'))).find((l: { recruitName: string }) => l.recruitName === `Lucas ${run}`)

  const page = await (await browser.newContext({ viewport: { width: 375, height: 740 }, locale: 'pl-PL' })).newPage()
  await page.goto(link.url)
  await expect(page.getByRole('heading', { name: 'Book your recruitment interview' })).toBeVisible() // the link's language wins over the browser's
  await expect(page.getByRole('radiogroup', { name: 'Choose a day' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

  await page.getByText('PL', { exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Zarezerwuj rozmowę rekrutacyjną' })).toBeVisible()
})

test('an unknown link says so instead of showing a booking form', async ({ page }) => {
  await page.goto('/book/not-a-real-token')
  await expect(page.getByText(/Ten link jest nieprawidłowy|This link is not valid/)).toBeVisible()
  await expect(page.getByRole('button', { name: /Rezerwuję|Book this time/ })).toHaveCount(0)
})

test('times next to an interview already booked are suggested first', async ({ page }) => {
  const run = runId()
  const { admin, section } = await club(run)
  const links = await json(admin.post('/api/booking-links', { data: { sectionIds: [section.id], count: 2 } }))
  const slots = await json(admin.get(`/api/public/links/${links[0].token}/slots`))
  await json(admin.post(`/api/public/links/${links[0].token}/bookings`, { data: { start: slots[0].start, fullName: `First ${run}`, email: `first.${run}@x.e2e`, gdprConsent: true } }))
  const next = (await json(admin.get(`/api/public/links/${links[1].token}/slots`))).find((s: { start: string }) => +new Date(s.start) === +new Date(slots[0].start) + 30 * 60e3)
  expect(next.suggested).toBe(true)

  await page.goto(`/book/${links[1].token}`)
  const suggested = page.getByRole('group', { name: 'Proponowane godziny' })
  await expect(suggested.getByRole('button').first()).toBeVisible()
  await expect(page.getByRole('group', { name: 'Pozostałe godziny' })).toBeVisible()
  await suggested.getByRole('button').first().click()
  await expect(page.getByRole('textbox', { name: 'Imię i nazwisko' })).toBeVisible()
})
