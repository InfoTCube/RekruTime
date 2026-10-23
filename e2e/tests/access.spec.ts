import { expect, test } from '@playwright/test'
import { ADMIN, adminApi, json, linkIn, login, mail, member, MEMBER_PASSWORD, runId } from './helpers'

test('wrong password is refused with a message', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Email' }).fill(ADMIN.email)
  await page.getByRole('textbox', { name: 'Password' }).fill('wrong')
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByText('Wrong email or password.')).toBeVisible()
})

test('admin adds a member, who sets a password from the e-mail and is logged in', async ({ page, browser }) => {
  const run = runId()
  const email = `nina.${run}@club.e2e`
  await login(page, ADMIN.email, ADMIN.password)
  await page.getByRole('link', { name: 'Users' }).click()
  await page.getByRole('button', { name: 'Add user' }).click()
  await page.getByRole('textbox', { name: 'Name' }).fill(`Nina ${run}`)
  await page.getByRole('textbox', { name: 'Email' }).fill(email)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(`Invitation e-mailed to ${email}`)).toBeVisible()

  const invite = await mail(email, 'Your RekruTime account')
  const nina = await (await browser.newContext()).newPage()
  await nina.goto(linkIn(invite.Text, '/set-password'))
  await nina.getByRole('textbox', { name: 'New password', exact: true }).fill(MEMBER_PASSWORD)
  await nina.getByRole('textbox', { name: 'Repeat new password' }).fill(MEMBER_PASSWORD)
  await nina.getByRole('button', { name: 'Save and log in' }).click()
  await expect(nina).toHaveURL(/\/interviews$/)
  await expect(nina.getByText(`Nina ${run}`).first()).toBeVisible()

  // The link works once
  await nina.goto(linkIn(invite.Text, '/set-password'))
  await nina.getByRole('textbox', { name: 'New password', exact: true }).fill('Other123!x')
  await nina.getByRole('textbox', { name: 'Repeat new password' }).fill('Other123!x')
  await nina.getByRole('button', { name: 'Save and log in' }).click()
  await expect(nina.getByText('This link is invalid or has expired')).toBeVisible()
})

test('recruiters see only their pages; section leaders also see Recruits', async ({ browser }) => {
  const run = runId()
  const admin = await adminApi()
  const section = await json(admin.post('/api/sections', { data: { name: `Web ${run}` } }))
  const recruiter = await member(admin, `Rita ${run}`, [section.id])
  const leader = await member(admin, `Leo ${run}`, [section.id], [section.id])

  const rita = await (await browser.newContext()).newPage()
  await login(rita, recruiter.email, MEMBER_PASSWORD)
  const nav = rita.getByRole('navigation')
  await expect(nav.getByRole('link', { name: 'Interviews' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'My availability' })).toBeVisible()
  for (const hidden of ['Recruits', 'Overview', 'Users', 'Import recruits', 'E-mail']) await expect(nav.getByRole('link', { name: hidden, exact: true })).toHaveCount(0)
  for (const path of ['/admin/users', '/recruits', '/overview']) {
    await rita.goto(path)
    await expect(rita).toHaveURL(/\/interviews$/) // sent home
  }

  const leo = await (await browser.newContext()).newPage()
  await login(leo, leader.email, MEMBER_PASSWORD)
  await leo.getByRole('navigation').getByRole('link', { name: 'Recruits', exact: true }).click()
  await expect(leo.getByRole('heading', { name: 'Recruits' })).toBeVisible()
  await expect(leo.getByText('Recruits who applied to the sections you lead.')).toBeVisible()
})
