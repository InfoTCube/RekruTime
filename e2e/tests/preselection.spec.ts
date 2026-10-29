import { expect, test } from '@playwright/test'
import { adminApi, json, login, member, MEMBER_PASSWORD, runId } from './helpers'

test('a section leader reads the answers and preselects applicants', async ({ page }) => {
  const run = runId()
  const admin = await adminApi()
  const section = await json(admin.post('/api/sections', { data: { name: `Rockets ${run}` } }))
  const leader = await member(admin, `Lena ${run}`, [section.id], [section.id])
  const form = await json(
    admin.post('/api/forms', {
      data: {
        title: `Form ${run}`,
        fields: [
          { id: 'sections', type: 'Sections', label: 'Sections', required: true },
          { id: 'why', type: 'Textarea', label: 'Why do you want to join?' },
        ],
      },
    }),
  )
  await json(admin.patch(`/api/forms/${form.id}`, { data: { isOpen: true } }))
  for (const name of [`Adam ${run}`, `Beata ${run}`])
    await json(
      admin.post(`/api/public/forms/${form.id}/responses`, {
        data: { fullName: name, email: `${name.split(' ')[0].toLowerCase()}.${run}@x.e2e`, gdprConsent: true, answers: { sections: [String(section.id)], why: [`${name} likes rockets`] } },
      }),
    )

  await login(page, leader.email, MEMBER_PASSWORD)
  await page.getByRole('navigation').getByRole('link', { name: 'Preselection' }).click()
  await expect(page.getByText(`Adam ${run} likes rockets`)).toBeVisible()
  await expect(page.getByText('To review (2)')).toBeVisible()

  await page.getByRole('button', { name: `Preselect Adam ${run}`, exact: true }).click()
  await expect(page.getByText('To review (1)')).toBeVisible()
  await page.getByRole('checkbox', { name: `Select Beata ${run}` }).check()
  await page.getByRole('button', { name: 'Not preselected (1)' }).click()
  await expect(page.getByText('To review (0)')).toBeVisible()

  await page.getByText('Preselected (1)', { exact: true }).click()
  await expect(page.getByRole('link', { name: `Adam ${run}` })).toBeVisible()
  await expect(page.getByRole('link', { name: `Beata ${run}` })).toHaveCount(0)
})
