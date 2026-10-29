import { expect, test } from '@playwright/test'
import { ADMIN, adminApi, json, login, runId } from './helpers'

test('admin builds a form, previews it and can only open it with a sections question', async ({ page }) => {
  const run = runId()
  const admin = await adminApi()
  await json(admin.post('/api/sections', { data: { name: `Robotics ${run}` } }))
  const title = `Recruitment ${run}`

  await login(page, ADMIN.email, ADMIN.password)
  await page.getByRole('navigation').getByRole('link', { name: 'Forms' }).click()
  await page.getByRole('link', { name: 'New form' }).click()
  await page.getByRole('textbox', { name: 'Title' }).fill(title)

  const addQuestion = async (type: string) => {
    await page.getByRole('button', { name: 'Add question' }).click()
    await page.getByRole('menuitem', { name: type, exact: true }).click()
  }
  await addQuestion('Long text')
  await page.getByRole('textbox', { name: 'Question' }).last().fill('Why do you want to join?')
  await addQuestion('Single choice')
  await page.getByRole('textbox', { name: 'Question' }).last().fill('Year of study')
  await page.getByRole('textbox', { name: 'Options' }).fill('1\n2\n3+')
  await page.getByRole('button', { name: 'Move up' }).last().click() // "Year of study" first

  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('Form saved.')).toBeVisible()
  await expect(page).toHaveURL(/\/admin\/forms\/\d+$/)

  await page.getByRole('tab', { name: 'Preview' }).click()
  const preview = page.getByRole('tabpanel')
  await expect(preview.getByRole('heading', { name: title })).toBeVisible()
  await expect(preview.getByRole('textbox', { name: 'Imię i nazwisko' })).toBeVisible() // fixed fields, default language
  await expect(preview.getByRole('radio', { name: '3+' })).toBeVisible()
  const labels = await preview.locator('label').allTextContents()
  expect(labels.findIndex((l) => l.includes('Year of study'))).toBeLessThan(labels.findIndex((l) => l.includes('Why do you want to join?')))

  // No sections question → can't open
  await page.getByRole('link', { name: '← All forms' }).click()
  await page.getByRole('switch', { name: `${title} open` }).click({ force: true })
  await expect(page.getByText('An open form needs a sections question.')).toBeVisible()

  await page.getByRole('link', { name: title }).click()
  await addQuestion('Sections')
  const saved = page.waitForResponse((r) => r.request().method() === 'PUT' && r.url().includes('/api/forms/'))
  await page.getByRole('button', { name: 'Save' }).click()
  expect((await saved).ok()).toBe(true) // (the first save's toast may still be showing)
  await page.getByRole('tab', { name: 'Preview' }).click()
  await expect(page.getByRole('tabpanel').getByRole('checkbox', { name: `Robotics ${run}` })).toBeVisible()

  await page.getByRole('link', { name: '← All forms' }).click()
  await page.getByRole('switch', { name: `${title} open` }).click({ force: true })
  await expect(page.getByText('Form opened')).toBeVisible()
  await expect(page.getByRole('switch', { name: `${title} open` })).toBeChecked()
})
