# E-mail setup

RekruTime sends e-mails (booking-link invitations) through **any SMTP server**. Settings come from configuration –
in production as environment variables (`Email__Host`, …), never stored in the database.

| Setting | Example | Notes |
|---|---|---|
| `Email__Host` | `smtp.gmail.com` | empty = e-mail disabled (links can still be copied / exported) |
| `Email__Port` | `587` | |
| `Email__Security` | `StartTls` | `StartTls` (587), `SslOnConnect` (465), `None` (local only), `Auto` |
| `Email__Username` | `rekrutacja@kolo.uni.edu.pl` | |
| `Email__Password` | app password / SMTP key | |
| `Email__FromAddress` | `rekrutacja@kolo.uni.edu.pl` | |
| `Email__FromName` | `Koło XYZ – Rekrutacja` | |
| `Email__ReplyTo` | `rekrutacja@kolo.uni.edu.pl` | where recruits' replies go (useful with a relay) |
| `Email__DelayBetweenMessagesMs` | `3000` | pause between e-mails (provider rate limits) |

After changing settings restart the app, then **Admin → E-mail → Send test e-mail to me**. If it fails, the
SMTP error is shown there.

## Recommended – dedicated sending account + replies to the university address

If the university address can't send through apps (the common case), create a **separate account only for sending**
(e.g. a new Gmail like `rekrutacja.kolo.xyz@gmail.com` → option A steps, or a Brevo account → option B) and let replies
go to the university inbox. Recruits see the club's name as the sender; "Reply" goes to the university address:

```
od:            Koło XYZ – Rekrutacja <rekrutacja.kolo.xyz@gmail.com>     ← Email__FromName + Email__FromAddress
odpowiedz do:  kolo.xyz@pwr.edu.pl                                         ← Email__ReplyTo
```

Also set `Club__ContactEmail` to the university address – it's the `{contact}` in the invitation and on the booking page.

## Option A – via Gmail (club address if allowed, or the dedicated account)

Works if the university's Google Workspace allows app passwords.

1. Log into the club account → *Google Account → Security* → enable **2-Step Verification**.
2. *Security → App passwords* → create one ("RekruTime") → 16 characters.
   If this menu is missing, the university disabled app passwords → use option B.
3. Settings: host `smtp.gmail.com`, port `587`, security `StartTls`, username = the club address,
   password = the app password, from = the club address.
4. Limits: ~500 recipients/day (Workspace: 2000). Keep `DelayBetweenMessagesMs` ≥ 3000 for big batches.

## Option B – SMTP relay (e.g. Brevo), if Gmail doesn't work

1. Create a free Brevo account (300 e-mails/day) → *SMTP & API* → SMTP key.
2. Add the club address as a **sender** and confirm the verification e-mail.
3. Settings: host `smtp-relay.brevo.com`, port `587`, security `StartTls`, username = Brevo login,
   password = SMTP key, from = the club address, reply-to = the club address.
4. Send a test e-mail to a Gmail and an Outlook address and check spam. If it lands in spam, the university
   domain's DMARC policy rejects mail sent "on its behalf" by Brevo – then use a sender on Brevo's or the
   circle's own domain and keep **reply-to** = the club address, so answers still reach you.

Any other SMTP server (university mail server, Mailgun, Resend, …) works the same way.

## Development

`docker compose up -d` also starts **Mailpit**: every e-mail the app sends locally lands in
<http://localhost:8025> instead of real inboxes. `appsettings.Development.json` already points to it.
