# RekruTime – Implementation Plan

Recruitment app for a scientific circle. Stack: **React (Vite + TS)** frontend, **.NET 10** backend.

Delivery order:
1. **MVP – interview scheduling** (availability → booking links → booking → interview lists)
2. **Post-recruitment** – interview notes + acceptance approvals
3. **Recruitment forms** – public form → preselection → personal booking links
4. **Mailing** – booking link, confirmation, decision

Each phase ships on its own. Data model is designed so later phases only *add* tables/columns.

---

## 0. Tech decisions

| Area | Choice | Why |
|---|---|---|
| Backend | ASP.NET Core 10, Minimal APIs, one project | Small app; no layers/repositories/MediatR needed |
| DB | PostgreSQL + EF Core 10 (Npgsql) | `tstzrange` exclusion constraint prevents double booking at DB level; `jsonb` for forms later |
| Auth | ASP.NET Core Identity, cookie auth, roles `Admin`, `Recruiter` | Built in. No public registration – admin creates accounts |
| Frontend | React + Vite + TypeScript, React Router, TanStack Query | Standard, minimal |
| Calendar UI | FullCalendar (`timeGrid` + `interaction`, MIT) | Drag-to-select availability for free |
| UI kit | Mantine (or plain CSS) | Forms, tables, modals without writing them |
| Hosting | Docker Compose: API + Postgres. Frontend built into API `wwwroot` | Same origin → no CORS, cookie auth just works. Dev: Vite proxy `/api` → backend |
| Time | Store UTC (`timestamptz`), display in club timezone (config, e.g. `Europe/Warsaw`) | Avoids DST bugs |

Repo layout:
```
/backend/RekruTime.Api        Program.cs, Data/ (DbContext, entities, migrations), Features/<Feature>/*.cs
/backend/RekruTime.Tests      xUnit – slot algorithm + booking tests
/frontend                     Vite app
docker-compose.yml
```

---

## 1. MVP – Interview scheduling

### 1.1 Domain rules (the core – get these right)

- **Roles**: `Admin` (= board member) and `Recruiter`. Admins also get sections assigned.
- Users ↔ Sections is many-to-many, managed by admins.
- Each section has `InterviewDurationMinutes` (default 30) and optional `BufferMinutes` (default 0) – room time after the interview for notes/changeover.
- **One room** → interviews (incl. buffer) can never overlap globally, regardless of section. Multiple rooms + online come later (see Future).
- **Recruitment period**: one global date range (club time zone, inclusive) set by an admin. Interviews can only be booked for times inside it; no period set = booking closed.
- A booking link is **individual** (one recruit, one booking) and targets a **set of sections** `S` (1 or more).
- For a link: `D = max(duration in S)`, `B = max(buffer in S)`. Room is blocked for `[t, t+D+B)`; interviewers must be available for the whole `[t, t+D+B)`.

**Relevant people for S** = users who belong to at least one section in S, plus all admins.

**Green slot** `[t, t+D+B)` – no interview overlaps it, and there is a team `T` of available relevant people with:
- `|T| ≥ 2`, and
- every section in S has at least one member of T belonging to it.

This gives exactly the required combinations for a single section: *2 people from section* or *admin + person from section* (two admins where one is in the section also qualifies). For multiple sections: someone from each section must be present (a person in several sections covers all of them).

**Yellow slot** – not green, but S is fully covered by **one** available person (single section: exactly one person from the section). Booking it creates an interview with that **one interviewer** – that's accepted, no follow-up needed. Yellow slots are **hidden by default**; the recruit sees them only after ticking *"None of these times suit me"*.

**Available** = the person's availability blocks fully cover `[t, t+D+B)` (blocks merged per user first, so 10:00–10:30 + 10:30–11:00 counts as 10:00–11:00).

**Team selection** when several teams qualify: smallest team, then fewest already-assigned interviews in the recruitment (load balancing), then random. Brute force over pairs/triples – candidate count is in the tens, it's fine.

### 1.2 Data model (EF Core)

```
AppUser : IdentityUser       DisplayName
RecruitmentSettings          Id = 1 (single row), InterviewsFrom?, InterviewsTo?   (DateOnly, club time zone)
Section                      Id, Name, InterviewDurationMinutes = 30, BufferMinutes = 0
UserSection                  UserId, SectionId                 (PK both)
Availability                 Id, UserId, Start, End            (UTC)
BookingLink                  Id, Token (unique), RecruitId?, Label, CreatedById, CreatedAt, ExpiresAt?, IsActive
BookingLinkSection           BookingLinkId, SectionId
Recruit                      Id, FullName, Email (unique), Phone?, CreatedAt
Application                  Id, RecruitId, SectionId, Status, CreatedAt   (unique RecruitId+SectionId)
Interview                    Id, RecruitId, BookingLinkId?, Start, End, BlockedUntil (= End + buffer),
                             Status (Scheduled|Cancelled|Done), CreatedAt
InterviewSection             InterviewId, SectionId
InterviewInterviewer         InterviewId, UserId
```

`Application` (recruit × section) is introduced now even though MVP barely uses it – phases 2 and 3 hang off it. Booking creates one Application per section in S.

**Individual links**: admin pastes a list of `name, email` lines (or just a count for anonymous links) → one link per line, `Recruit` created up front when known (booking form prefilled). Export as CSV / copy-all so they can be sent manually until mailing exists.

DB constraints (raw SQL in migration):
```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
ALTER TABLE "Interviews" ADD CONSTRAINT no_overlap
  EXCLUDE USING gist (tstzrange("Start","BlockedUntil") WITH &&) WHERE ("Status" <> 'Cancelled');
CREATE UNIQUE INDEX one_booking_per_link ON "Interviews" ("BookingLinkId") WHERE ("Status" <> 'Cancelled');
CHECK ("End" > "Start") on Availability and Interviews
```
→ two recruits clicking the same slot at once: second insert fails → API returns `409 Slot taken`.
→ link used twice (double click, two tabs): second insert fails → `409 Already booked`. If an admin cancels the interview, the link works again (lets the recruit rebook).

Booking link token: 128-bit random, base64url (`RandomNumberGenerator`). Not guessable, not sequential.

### 1.3 API

Auth / users
- `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`
- `GET/POST/PUT/DELETE /api/users` (Admin) – create user (set-password e-mail, temp password without e-mail – #37), set role, assign sections
- `GET/POST/PUT/DELETE /api/sections` (Admin; GET for all logged in)

Recruitment period
- `GET /api/settings/recruitment` (any logged in), `PUT` (Admin) `{interviewsFrom, interviewsTo}` – both set or both null

Availability (Recruiter/Admin, own data)
- `GET /api/availability?from&to`
- `POST /api/availability` `{start,end}` – merged with adjacent/overlapping blocks server-side
- `DELETE /api/availability/{id}` – also supports cutting a range out
- Reject removal of availability overlapping the user's scheduled interview (tell them to contact admin)

Booking links (Admin)
- `GET /api/booking-links?sectionId&used` – list with status (unused / booked / expired)
- `POST /api/booking-links` `{sectionIds[], expiresAt?, recipients: [{name?, email?}] | count}` → one link per recipient, returns URLs `https://app/book/{token}`
- `PATCH /api/booking-links/{id}` – deactivate
- `GET /api/booking-links/{id}/slots?from&to` – preview coverage (same calc as public; admins use it to spot gaps)

Public (no auth)
- `GET /api/public/links/{token}` → sections, duration, contact email, prefilled name/email, existing booking (if already used → show it instead of the calendar)
- `GET /api/public/links/{token}/slots?from&to&includeYellow=false` → `[{start, end, color: green|yellow}]` (no interviewer names exposed)
- `POST /api/public/links/{token}/bookings` `{start, fullName, email, phone?, gdprConsent}` → recomputes slot inside a transaction, picks team, inserts interview; 409 if taken/no longer valid
- Rate-limit public endpoints (`AddRateLimiter`, built in)

Interviews
- `GET /api/interviews?from&to&sectionId` – Recruiter: only their own; Admin: all
- `PATCH /api/interviews/{id}` (Admin) – cancel, change interviewers, reschedule

### 1.4 Slot algorithm (`SlotCalculator`, pure function → unit tested)

```
input: sections S, duration D, buffer B, range [from,to] ∩ recruitment period, step = 15 min, minNotice (setting on the Recruitment period page, default 12 h)
people   = relevant users for S with their section sets + isAdmin
blocks   = merged availability per person in range
busy     = scheduled interviews in range, as [Start, BlockedUntil)
for t in [max(from, now+minNotice), to - D - B] step 15min:
    if overlaps(busy, t, t+D+B): continue
    avail = people whose merged blocks cover [t, t+D+B)
    if findTeam(avail, S, minSize=2): emit green
    elif any p in avail covers all S:  emit yellow
```
Same function is used by the booking endpoint to validate + pick the team, so preview, public view and booking can't disagree.

**Suggested slots (#110)** – so interviewers travel once and do several interviews in a row:
- A green slot is *suggested* when it sits next to a scheduled interview on the same day (starts when that interview's block ends or ends when the next one starts, gap ≤ 30 min) and its team includes at least one interviewer of that interview.
- `findTeam` tie-break: people who already have an interview that day first, then fewest assigned interviews, then id.
- Booking page lists suggested times first ("Suggested times"), other green times below, yellow still behind the toggle. Only green slots are suggested; an empty day has none. Always on.

Unit tests (the one place to test hard): single section 2 recruiters; admin + recruiter; admin from other section + nobody from section (none); two sections one person each; person in both sections alone (yellow); interview blocks slot for all sections; buffer blocks the following slot; adjacent blocks merge; duration longer than block; DST day.

### 1.5 Frontend pages

Logged in:
- `/login`
- `/availability` – FullCalendar week view limited to the recruitment period; drag to add, click to remove; own interviews shown read-only on top
- `/admin/recruitment` – set the recruitment period (two date inputs) or close booking
- `/interviews` – list/calendar of my interviews (Admin: all, with section filter)
- `/admin/users`, `/admin/sections` (name, duration, buffer), `/admin/links` (bulk create from pasted list, copy/CSV export, used/unused status, deactivate, coverage preview)

Public:
- `/book/:token` – sections shown at top; day picker + list of start-time buttons (a list is clearer on mobile than overlapping calendar events); choose → form (name, email, phone, GDPR consent; prefilled if known) → confirmation screen with date, time, room.
- Only green times shown by default. Below them a toggle *"None of these times suit me"* → reveals yellow times (marked yellow) and the text *"If none of these times suit you either, contact us at {email}."*
- Link already used → show the booked interview instead of the calendar. No self-cancel in MVP (contact by email).

### 1.6 MVP tasks (in order)

1. Scaffold: solution, Vite app, docker-compose Postgres, Vite proxy, serve SPA from API in prod
2. Identity + roles + seed first admin from config; login page; route guards
3. Sections + users admin CRUD + section assignment
4. Availability API + FullCalendar editor
4b. Recruitment period setting (#50)
5. `SlotCalculator` + unit tests
6. Individual booking links (bulk create, CSV export) + public slots endpoint + booking page with yellow toggle
7. Booking endpoint with exclusion constraint + one-booking-per-link index + 409 handling
8. Interviews list (recruiter/admin), admin cancel/reassign
9. Deploy (Docker image, HTTPS via reverse proxy, DB backups)

### 1.7 Import & invitations (pulled into the MVP from phases 3–4)

The club's applications come from a Google Form, so the MVP closes with:
- **E-mail sending** (#62): any SMTP server from config (club Gmail with app password, or a relay like Brevo as the fallback – `docs/EMAIL.md`); `EmailMessage` outbox + background sender with retry/backoff and throttling; admin page with a test e-mail and statuses; Mailpit in dev.
- **Invitation templates per language** (#63): subject/body with placeholders, Polish default + English; `BookingLink.Language`.
- **Import** (#64): paste/upload the Forms CSV, map columns (name, e-mail, sections, language), merge rows by e-mail, **preview** (unknown section / invalid e-mail / already invited), then create links and queue invitations in one go; re-import is idempotent.
- **Invitation status & resend** on the links page (#65).
- **Booking page in Polish + English** (#66), dictionary per language.

Phase 3 (forms) then replaces the CSV with RekruTime's own form; phase 4 adds confirmation/decision e-mails on top of the same outbox.

---

## 2. Post-recruitment: notes + approvals

### Data
```
UserSection                  + IsLeader (bool)
InterviewNote                Id, InterviewId, AuthorId, SectionId?, Text, UpdatedAt   (one per author per interview)
Application.Status           Pending | Disputed | Accepted | Rejected (+ later: Submitted/Preselected for forms)
ApplicationVote              Id, ApplicationId, UserId, Decision (Approve|Reject), IsFinal, Comment, CreatedAt
                             unique (ApplicationId, UserId)
```

### Rules
Voters on an application: **leaders of that section** and **admins**.

- **Accepted** – Approve votes from a section leader and an admin who are **two distinct users** (an admin who is also the leader counts once → a second admin is required).
- **Rejected** – same, both Reject.
- **Disputed** – a leader/admin pair split (one Approve, one Reject). Then **another admin** (not one of the voters so far) makes the **final decision**, which sets Accepted/Rejected directly.

Status is recomputed on every vote and stored (simple filtering). Votes can be changed until a final decision is made – so an agreed Accepted/Rejected turns Disputed if another leader/admin votes the other way. Votes of users who are no longer leaders/admins don't count; deleting a user deletes their votes.

### API / UI
- `GET/PUT /api/interviews/{id}/notes` – interviewers of that interview write; leaders of its sections + admins read
- `GET /api/recruits?sectionId&status` – leaders see their sections, admins all
- `POST /api/applications/{id}/votes` – normal vote; on a Disputed application an admin who hasn't voted gets a "Final decision" action (`IsFinal = true`)
- Pages: interview detail with notes editor (autosave), `/recruits` table (name, sections, interview date, interviewers, status per section, votes) → detail page with all notes + vote buttons

---

## 3. Recruitment forms

### Data
```
Form                 Id, Title, Description, IsOpen, Schema (jsonb: [{id, type, label, required, options}]), CreatedAt
FormResponse         Id, FormId, RecruitId, Answers (jsonb: [{fieldId, values[]}]), SubmittedAt   unique (FormId, RecruitId)
Application.Status   + Submitted | Preselected | NotPreselected | InterviewInvited
```
Field types: text, textarea, email, phone, single choice, multi choice, **sections** (special: options = sections, creates Applications). No form-builder library – a simple list editor over the JSON schema.

Admin API (#27): `GET/POST /api/forms`, `GET/PUT/PATCH {isOpen}/DELETE /api/forms/{id}`.
- Full name, e-mail and the GDPR consent are fixed on every form (like the booking page); the schema holds only the extra questions.
- Answers are keyed by the field id (server-made when left out), so renaming/reordering questions keeps them.
- At most one sections question; a form can only be open with one. A form with responses can't be deleted (close it); a recruit's GDPR delete takes their responses.

### Flow
1. Admin creates form → public URL `/form/:id`
2. Submit → upsert Recruit by email, FormResponse, Application per chosen section (`Submitted`)
   - Public API (#29): `GET /api/public/forms/{id}` (410 when closed), `POST /api/public/forms/{id}/responses` – rate-limited like booking, consent required, every answer validated against its question. Submitting again replaces the answers; a section un-ticked then drops its application only while it is still `Submitted`. The page language is saved with the response (for the invitation). A Phone answer fills `Recruit.Phone`.
3. **Preselection page** `/preselection` (#30): per section, applicants with their latest form answers; mark Preselected / NotPreselected / back to Submitted, one by one or in bulk (leaders for their sections, admins all). API `GET /api/preselection?sectionId`, `POST /api/preselection {applicationIds, status}` – applications outside preselection (imported, invited) are skipped. Votes are refused while an application is Submitted / Preselected / NotPreselected.
4. "Invite to interview" (bulk) → creates an individual BookingLink per recruit with `RecruitId` set and S = their preselected sections. Until phase 4: copy links / export CSV.

Reuses the MVP booking flow and link model unchanged – links are already individual.

### Interview prep: answers + suggested questions (#93)
The interview page (and recruit detail) shows all of the recruit's form answers next to **suggested questions**:
- per section – a question list admins/section leaders maintain, shown for the interview's sections
- per form field (optional) – a follow-up question set in the form builder, shown under the recruit's answer

Same visibility as notes: interviewers, leaders of the interview's sections, admins.

---

## 4. Mailing

### Design
- `EmailMessage` outbox table: `Id, To, Subject, Body, Status (Pending|Sent|Failed), Attempts, LastError, CreatedAt, SentAt`
- `BackgroundService` sends pending emails with retry; nothing is sent inside request transactions → booking never fails because SMTP is down
- Transport: SMTP via **MailKit** (club Google Workspace / university SMTP), or Resend/Postmark if deliverability is an issue
- Templates: stored in DB, editable by admins, simple `{{Name}}`, `{{Link}}`, `{{Date}}` placeholders (string replace, no template engine)

### Emails
| Trigger | Content |
|---|---|
| "Invite to interview" (phase 3) or manual send from admin links page | Personal booking link |
| Booking created (#35) | Confirmation: date, time, place, link, `.ics` attachment (template `confirmation`) |
| Booking created (#35) | Notice to the assigned interviewers (fixed English text, link to the interview, `.ics`) |
| – | Recruit self-cancel/reschedule: **not built** (#35 decision) – recruits write to `{contact}`, an admin cancels and the link works again |
| Admin cancels/moves/swaps interviewers (#35, "notify" on by default) | Recruit: `rescheduled` / `cancelled` (with the link to rebook); team: moved / cancelled / added / removed notices. `.ics` with a stable UID updates or cancels the calendar event |
| 24 h before (#38, switch on the E-mail page, **off** by default) | Reminder to the recruit (template `reminder`), once per interview; again after a move; skipped when booked/moved within the last day |
| Admin clicks "Send decision" (#36) | Accepted / rejected email – **manual trigger**, never automatic on vote. One per recruit once all their applications are decided: "accepted" (listing accepted sections) if any, else "rejected". Sending locks the votes. Templates `accepted`/`rejected` per language, editable like the invitation |
| User created / admin resets a password (#37) | Link to set the password (`/set-password`, Identity reset token, once, 3 days; logs in). Temp password only when e-mail isn't set up |

---

## Cross-cutting

- **GDPR**: consent checkbox on booking/form, privacy note, admin "delete recruit" (cascades), purge recruits after recruitment ends (manual action is enough at first)
- **Security**: public endpoints rate-limited; tokens random; recruiters only see their own interviews / notes they're allowed to; validate all public input
- **Audit**: `CreatedAt/By` on interviews, votes, links – enough; no separate audit log
- **Tests**: unit tests for pure logic; integration tests through HTTP against real Postgres (shared container, DB per test, `Testing` env) for every feature + an authorization matrix over all endpoints (#97); CI on every PR. Browser E2E (Playwright, `e2e/`, #98) of the whole flow in CI too.

## Decisions log

1. 3+ sections: a 3-person team is fine when 2 people can't cover all sections (minimal covering team).
2. Yellow slot → interview with one recruiter, no follow-up. Yellow times hidden behind the "None of these times suit me" toggle.
3. Links are individual – one link = one booking (DB-enforced).
4. Recruit self-cancel/reschedule: out of MVP.
5. Buffer between interviews: optional per section (`BufferMinutes`, default 0).
6. Rejection needs leader + admin both rejecting; split vote → another admin has the final say.
7. One room for now.
8. Booking only for interview dates inside one global recruitment period set by an admin (#50).
9. Recruits are imported from the Google Form export and invited by e-mail with personal links (#62–#66).
10. E-mail via any SMTP server: club Gmail first, a relay (Brevo) as the fallback; CSV export always works.
11. Recruit-facing texts in Polish by default, English included, more languages = one dictionary file each.
12. No recruit self-cancel/reschedule link (yet): recruits write to the contact e-mail, an admin cancels (the link reopens) or moves the interview – both e-mail the recruit.

## Future (not planned in detail)

- **No-shows**: an interview status `NoShow` set by an interviewer/admin, with a one-click e-mail to the recruit carrying a link to book again (the link reopens like after a cancel).
- **Interviewer swaps**: an interviewer marks "I need a replacement"; members of the interview's sections who are free then see it and take it over with one click (team change + the usual moved/added/removed notices). No admin needed.
- **Capacity warnings + availability nudge**: Overview warns when invited-but-unbooked recruits outnumber the free green slots (per section); a button e-mails members of a section who have no availability in the recruitment period.
- **Message a group of recruits**: a one-off e-mail to a filtered list (section, status, booked or not) through the outbox, with the usual placeholders.
- **Recruitment funnel + history**: per-section counts applied → interviewed → accepted, kept as anonymous numbers (per recruitment season) when the GDPR purge deletes the people, so the next board can compare years.
- **Discord notifications**: webhook URL setting; posts to a channel on new bookings, disputed applications waiting for a final decision, and the accepted list after decisions are sent. No recruit contact data in the posts.
- **University single sign-on for members**: log in with the university account instead of a RekruTime password (protocol to check with the university IT – OIDC/SAML/CAS; configurable, so other clubs can point it at their university). Accounts still created by admins; SSO only replaces the password.
- **Telemetry (#92)**: OpenTelemetry in the API (requests, EF, e-mail sender) to a self-hosted dashboard or an error tracker, frontend error reporting, a health check with alerting, simple usage counters. No recruit personal data in telemetry.
- **Multiple rooms + online interviews**: add `Room` (incl. an "Online" kind with unlimited capacity), `Interview.RoomId`, and change the exclusion constraint to `EXCLUDE USING gist ("RoomId" WITH =, tstzrange(...) WITH &&)`. Then interviewers need their own no-overlap check (today the single room guarantees it). Slot calc picks any free room. Kept out now – one room makes everything simpler.
