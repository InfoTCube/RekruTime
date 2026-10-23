import { expect, request, type APIRequestContext, type Page } from '@playwright/test'
import { config } from '../config.mjs'

export const ADMIN = config.admin
export const MEMBER_PASSWORD = 'Member123!'

// Unique per spec run, so names and e-mails never collide in the shared database / Mailpit.
export const runId = () => Math.random().toString(36).slice(2, 7)

// ---- Mailpit ----

export type Mail = { ID: string; Subject: string; Text: string; HTML: string; Attachments: { FileName: string; ContentType: string; PartID: string }[] }

// Waits for the newest e-mail to `to` whose subject contains `subject` (Mailpit search syntax).
export async function mail(to: string, subject: string): Promise<Mail> {
  let found: Mail | undefined
  await expect
    .poll(
      async () => {
        const r = await fetch(`${config.mailpit.api}/search?query=${encodeURIComponent(`to:"${to}" subject:"${subject}"`)}`)
        const list = (await r.json()) as { messages: { ID: string }[] }
        if (!list.messages.length) return false
        found = (await (await fetch(`${config.mailpit.api}/message/${list.messages[0].ID}`)).json()) as Mail
        return true
      },
      { timeout: 30_000, message: `e-mail to ${to} with "${subject}"` },
    )
    .toBe(true)
  return found!
}

export async function attachment(m: Mail, fileName: string): Promise<string> {
  const part = m.Attachments.find((a) => a.FileName === fileName)
  expect(part, `${fileName} attached`).toBeTruthy()
  return (await fetch(`${config.mailpit.api}/message/${m.ID}/part/${part!.PartID}`)).text()
}

export const linkIn = (text: string, path: string) => {
  const m = text.match(new RegExp(`${config.baseURL}${path}\\S+`))
  expect(m, `${path} link in the e-mail`).toBeTruthy()
  return m![0]
}

// ---- API (setup that isn't what a test is about) ----

export async function apiAs(email: string, password: string): Promise<APIRequestContext> {
  const ctx = await request.newContext({ baseURL: config.baseURL })
  const r = await ctx.post('/api/auth/login', { data: { email, password } })
  expect(r.ok(), `API login ${email}`).toBeTruthy()
  return ctx
}

export const adminApi = () => apiAs(ADMIN.email, ADMIN.password)

export async function json<T = any>(call: Promise<import('@playwright/test').APIResponse>): Promise<T> {
  const r = await call
  expect(r.ok(), `${r.url()} → ${r.status()} ${await r.text()}`).toBeTruthy()
  return r.status() === 204 ? (undefined as T) : r.json()
}

export type Member = { id: string; email: string; name: string; api: APIRequestContext }

// A club member created by the admin; sets their password through the invite link, like a real person would.
export async function member(admin: APIRequestContext, name: string, sectionIds: number[], leads: number[] = []): Promise<Member> {
  const email = `${name.toLowerCase().replace(/\s+/g, '.')}@club.e2e`
  const created = await json(admin.post('/api/users', { data: { email, displayName: name, role: 'Recruiter', sectionIds, leaderSectionIds: leads } }))
  const invite = await mail(email, 'Your RekruTime account')
  const link = new URL(linkIn(invite.Text, '/set-password'))
  const api = await request.newContext({ baseURL: config.baseURL })
  await json(api.post('/api/auth/set-password', { data: { userId: link.searchParams.get('u'), token: link.searchParams.get('t'), newPassword: MEMBER_PASSWORD } }))
  return { id: created.user.id, email, name, api }
}

// A section, an open recruitment period and two recruiters free 10:00–14:00 (club time) three days from now.
export async function club(run: string) {
  const admin = await adminApi()
  const section = await json(admin.post('/api/sections', { data: { name: `AI ${run}`, interviewDurationMinutes: 30 } }))
  const today = new Date()
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  await json(admin.put('/api/settings/recruitment', { data: { interviewsFrom: iso(today), interviewsTo: iso(new Date(+today + 14 * 864e5)) } }))
  const leader = await member(admin, `Lena ${run}`, [section.id], [section.id])
  const recruiter = await member(admin, `Igor ${run}`, [section.id])
  const day = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() + 3, 9)) // 10:00 Warsaw (winter) / 11:00 (summer)
  for (const m of [leader, recruiter])
    await json(m.api.post('/api/availability', { data: { start: day.toISOString(), end: new Date(+day + 4 * 3600e3).toISOString() } }))
  return { admin, section, leader, recruiter }
}

// ---- UI ----

export async function login(page: Page, email: string, password: string) {
  await page.goto('/login')
  await page.getByRole('textbox', { name: 'Email' }).fill(email)
  await page.getByRole('textbox', { name: 'Password' }).fill(password)
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page).not.toHaveURL(/\/login/)
}
