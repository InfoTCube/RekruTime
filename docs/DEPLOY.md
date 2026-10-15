# Deployment (Docker)

Production = one app container (React + API, image `ghcr.io/infotcube/rekrutime`, built by GitHub Actions on every
push to `main`) + Postgres. Database migrations run automatically on startup.

## First start

```bash
git clone https://github.com/InfoTCube/RekruTime.git rekrutime-deploy && cd rekrutime-deploy
cp .env.example .env     # fill in: POSTGRES_PASSWORD, SeedAdmin__*, Club__*, Email__* (see docs/EMAIL.md)
docker compose -f docker-compose.prod.yml up -d
```

Only `docker-compose.prod.yml`, `.env` (and `Caddyfile` for the Caddy option) are needed – in Dockge/Portainer you can
paste the compose file into a new stack and fill in its `.env` there.

Log in with `SeedAdmin__Email` / `SeedAdmin__Password` – use a long, unique password: users can't change their own
password in the app yet (#83). The seed admin is created only when no admin exists, so you can clear
`SeedAdmin__Password` afterwards.

## Exposing it

The app listens on `127.0.0.1:8080` (`APP_PORT`) and expects a reverse proxy in front of it – it trusts
`X-Forwarded-For` / `X-Forwarded-Proto` (client IP for rate limiting, https for cookies), so **don't publish port 8080
to the internet directly**. Recruits open booking links without logging in, so the app must be reachable publicly
at `Club__PublicUrl`.

- **Bare VPS with a domain:** `docker compose -f docker-compose.prod.yml --profile caddy up -d` also starts Caddy with
  automatic HTTPS for `DOMAIN` (DNS A record → server, ports 80/443 open).
- **Existing proxy / tunnel** (nginx, Traefik, Cloudflare Tunnel, …): point it at `http://127.0.0.1:8080` and make
  sure it sends `X-Forwarded-For` and `X-Forwarded-Proto` (Caddy, Traefik and cloudflared do by default; nginx needs
  `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for; proxy_set_header X-Forwarded-Proto $scheme;`).
- **Server only reachable through Tailscale** (no public IP / open ports): use [Tailscale Funnel](https://tailscale.com/kb/1223/funnel) –
  see below.

### Tailscale Funnel

Funnel publishes one port of a tailnet machine on the internet at `https://<machine>.<tailnet>.ts.net`, with a
certificate from Tailscale – no domain, DNS or router changes. It sends the visitor's real IP in `X-Forwarded-For`
and `X-Forwarded-Proto: https`, which is what the app expects.

1. Tailnet admin console: enable **MagicDNS** and **HTTPS certificates** (*DNS* page), and allow Funnel for the
   server in the access policy (`"nodeAttrs": [{"target": ["<server>"], "attr": ["funnel"]}]` – the console offers
   to add it the first time you run the command below).
2. On the server, after the stack is running:
   ```bash
   sudo tailscale funnel --bg 8080              # https://<machine>.<tailnet>.ts.net → 127.0.0.1:8080, survives reboots
   tailscale funnel status                      # shows the public URL
   ```
   If port 443 is already used by another `tailscale serve`/`funnel` on that machine, use
   `sudo tailscale funnel --bg --https=8443 8080` (URL gets `:8443`; Funnel allows 443, 8443 and 10000).
3. Set `Club__PublicUrl` in `.env` to that URL and `docker compose -f docker-compose.prod.yml up -d` again.
4. Check from a phone **outside** the tailnet (mobile data) that `<url>/api/health` answers.

The whole app is public this way (as with any other option); the panel is protected by login with lockout after
failed attempts.

### Dockge / Portainer

Create a stack (e.g. `rekrutime`), paste `docker-compose.prod.yml` as its compose file and the filled `.env` as its
environment. Backups go to `backups/` inside the stack folder. Updating = *Update* / *Pull and redeploy* in the UI
(same as `pull` + `up -d`).

## Updating / rollback

```bash
docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d
```

To roll back, set `IMAGE_TAG=sha-<commit>` in `.env` and run the same command.

## Monitoring

`GET /api/health` → `200 {"status":"ok"}` when the app can reach the database (also used by the container
healthcheck). Add it to Uptime Kuma as an HTTP monitor: `http://localhost:8080/api/health`.

## Data & backups

The `backup` service dumps the database **daily** to `./backups/rekrutime-YYYY-MM-DD.dump` (next to the compose
file) and keeps 14 days; the first dump is made right after start. `docker compose -f docker-compose.prod.yml logs backup`
shows `backup ok` / `backup FAILED`. Copy `./backups` off the server regularly – a backup on the same disk doesn't
survive a dead disk.

| Volume / folder | Contents |
|---|---|
| `pgdata` | the database (dumped daily to `./backups`) |
| `./backups` | daily dumps, 14 days |
| `keys` | login-cookie encryption keys (losing them only logs everyone out) |
| `caddy_data` | HTTPS certificates (Caddy option) |

```bash
# extra backup right now
docker compose -f docker-compose.prod.yml exec -T db pg_dump -U rekrutime -Fc rekrutime > rekrutime-now.dump
# restore (replaces the current data)
docker compose -f docker-compose.prod.yml exec -T db pg_restore -U rekrutime -d rekrutime --clean --if-exists < backups/rekrutime-YYYY-MM-DD.dump
```
