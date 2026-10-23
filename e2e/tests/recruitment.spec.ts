import { devices, expect, test, type Page } from '@playwright/test'
import { ADMIN, attachment, club, linkIn, login, mail, MEMBER_PASSWORD, runId } from './helpers'

// The whole recruitment, in order: import → invitation → booking from the e-mail → note → votes → decision e-mail.
test.describe.serial('recruitment flow', () => {
  const run = runId()
  const recruit = { name: `Ewa Test ${run}`, email: `ewa.${run}@recruit.e2e` }
  let setup: Awaited<ReturnType<typeof club>>

  test.beforeAll(async () => {
    setup = await club(run)
  })

  test('admin imports the applicants and invitations go out', async ({ page }) => {
    await login(page, ADMIN.email, ADMIN.password)
    await page.getByRole('link', { name: 'Import recruits' }).click()
    // As copied from the Google Forms sheet; columns are recognised from the headers
    await page.getByPlaceholder('Paste here…').fill(
      ['Imię i nazwisko,Adres e-mail,Sekcja', `${recruit.name},${recruit.email},AI ${run}`, `Piotr Test ${run},piotr.${run}@recruit.e2e,AI ${run}`].join('\n'),
    )
    await expect(page.getByText('2 rows found.')).toBeVisible()
    await page.getByRole('button', { name: 'Preview' }).click()
    await expect(page.getByText('2 new')).toBeVisible()
    await page.getByRole('button', { name: 'Create 2 links & send 2 e-mails' }).click()
    await expect(page.getByText('Import finished')).toBeVisible()

    const invitation = await mail(recruit.email, 'Zaproszenie na rozmowę rekrutacyjną')
    expect(invitation.Text).toContain(`AI ${run}`)
  })

  test('the recruit books from the link in the e-mail, on a phone', async ({ browser }) => {
    const invitation = await mail(recruit.email, 'Zaproszenie na rozmowę rekrutacyjną')
    const phone = await browser.newContext({ ...devices['Pixel 7'], locale: 'pl-PL', timezoneId: 'Europe/Warsaw' })
    const page = await phone.newPage()
    await page.goto(linkIn(invitation.Text, '/book/'))

    await expect(page.getByRole('heading', { name: 'Zarezerwuj rozmowę rekrutacyjną' })).toBeVisible()
    await page.getByRole('radiogroup', { name: 'Wybierz dzień' }).getByRole('radio').first().click()
    await page.getByRole('button', { name: /^\d{2}:\d{2}$/ }).first().click()
    await expect(page.getByRole('textbox', { name: 'Imię i nazwisko' })).toHaveValue(recruit.name) // known from the import
    await page.getByRole('textbox', { name: /Discord/ }).fill('ewa_test')
    await page.getByRole('checkbox', { name: /Wyrażam zgodę/ }).check()
    await page.getByRole('button', { name: 'Rezerwuję ten termin' }).click()
    await expect(page.getByText('Twoja rozmowa jest zarezerwowana')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true) // no sideways scrolling

    // Confirmation with a calendar event; both interviewers hear about it
    const confirmation = await mail(recruit.email, 'Potwierdzenie rozmowy rekrutacyjnej')
    expect(confirmation.Text).toContain('Miejsce: Sala 101')
    expect(await attachment(confirmation, 'invite.ics')).toContain('METHOD:PUBLISH')
    for (const m of [setup.leader, setup.recruiter]) await mail(m.email, `New interview: ${recruit.name}`)
  })

  test('an interviewer writes a note, saved automatically', async ({ page }) => {
    await login(page, setup.recruiter.email, MEMBER_PASSWORD)
    const card = page.locator('.mantine-Paper-root', { hasText: recruit.name })
    await card.getByRole('link', { name: 'Notes' }).click()
    await page.getByRole('textbox', { name: 'Your note' }).fill('Motivated, knows Python well. Good fit for AI.')
    await expect(page.getByText(/Saved at \d{2}:\d{2}/)).toBeVisible()
    await page.reload()
    await expect(page.getByRole('textbox', { name: 'Your note' })).toHaveValue('Motivated, knows Python well. Good fit for AI.')
  })

  test('the section leader and an admin approve, the admin sends the decision', async ({ browser }) => {
    const openRecruit = async (page: Page, email: string, password: string) => {
      await login(page, email, password)
      await page.getByRole('navigation').getByRole('link', { name: 'Recruits', exact: true }).click()
      await page.getByRole('link', { name: recruit.name }).click()
      await expect(page.getByRole('heading', { name: recruit.name })).toBeVisible()
    }

    const lena = await (await browser.newContext()).newPage()
    await openRecruit(lena, setup.leader.email, MEMBER_PASSWORD)
    await expect(lena.getByText('Motivated, knows Python well.')).toBeVisible() // the interviewer's note
    await lena.getByPlaceholder('Comment (optional)').fill('Strong candidate')
    await lena.getByRole('button', { name: 'Approve' }).click()
    await expect(lena.getByText('Strong candidate')).toBeVisible()
    await expect(lena.getByText('Pending', { exact: true })).toBeVisible() // a leader alone doesn't decide

    const admin = await (await browser.newContext()).newPage()
    await openRecruit(admin, ADMIN.email, ADMIN.password)
    await admin.getByRole('button', { name: 'Approve' }).click()
    await expect(admin.getByText('Accepted', { exact: true })).toBeVisible()
    await admin.getByRole('button', { name: 'Send decision e-mail' }).click()
    await admin.getByRole('dialog').getByRole('button', { name: 'Send' }).click()
    await expect(admin.getByText('Decision e-mail sent', { exact: true })).toBeVisible()

    const decision = await mail(recruit.email, 'Wynik rekrutacji')
    expect(decision.Subject).toBe('Wynik rekrutacji – witamy w kole!')
    expect(decision.Text).toContain(`AI ${run}`)

    await lena.reload()
    await expect(lena.getByText('Decision e-mail sent – votes are locked.')).toBeVisible()
    await expect(lena.getByRole('button', { name: /^Approve/ })).toHaveCount(0)
  })
})
