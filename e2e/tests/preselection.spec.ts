import { expect, test } from '@playwright/test'
import { ADMIN, adminApi, json, linkIn, login, mail, member, MEMBER_PASSWORD, runId } from './helpers'

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

test('an admin invites the preselected applicants and the invitation link books an interview', async ({ page, browser }) => {
  const run = runId()
  const admin = await adminApi()
  const section = await json(admin.post('/api/sections', { data: { name: `Satellites ${run}` } }))
  const form = await json(
    admin.post('/api/forms', { data: { title: `Form ${run}`, fields: [{ id: 'sections', type: 'Sections', label: 'Sections', required: true }] } }),
  )
  await json(admin.patch(`/api/forms/${form.id}`, { data: { isOpen: true } }))
  const email = `celina.${run}@x.e2e`
  await json(
    admin.post(`/api/public/forms/${form.id}/responses`, {
      data: { fullName: `Celina ${run}`, email, gdprConsent: true, language: 'en', answers: { sections: [String(section.id)] } },
    }),
  )
  const list = await json(admin.get(`/api/preselection?sectionId=${section.id}`))
  await json(admin.post('/api/preselection', { data: { applicationIds: [list.applicants[0].applicationId], status: 'Preselected' } }))

  await login(page, ADMIN.email, ADMIN.password)
  await page.goto('/preselection')
  await page.getByRole('button', { name: 'Invite preselected…' }).click()
  const dialog = page.getByRole('dialog', { name: 'Invite to interview' })
  await expect(dialog.getByRole('checkbox', { name: `Invite Celina ${run}` })).toBeChecked()
  await dialog.getByRole('button', { name: /^Invite \d+ & send e-mails$/ }).click()
  await expect(page.getByText(/links? created, \d+ invitations? queued/)).toBeVisible()

  // The recruit's invitation (in the form's language) opens their booking page for the section.
  const invitation = await mail(email, 'Invitation to a recruitment interview')
  const recruit = await (await browser.newContext()).newPage()
  await recruit.goto(linkIn(invitation.Text, '/book/'))
  await expect(recruit.getByRole('heading', { name: 'Book your recruitment interview' })).toBeVisible()
  await expect(recruit.getByText(`Satellites ${run}`)).toBeVisible()
})
