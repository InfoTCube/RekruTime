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

Log in with `SeedAdmin__Email` / `SeedAdmin__Password` and change the password. The seed admin is created only when
no admin exists, so you can clear `SeedAdmin__Password` afterwards.

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

## Updating / rollback

```bash
docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d
```

To roll back, set `IMAGE_TAG=sha-<commit>` in `.env` and run the same command.

## Monitoring

`GET /api/health` → `200 {"status":"ok"}` when the app can reach the database (also used by the container
healthcheck). Add it to Uptime Kuma as an HTTP monitor: `http://localhost:8080/api/health`.

## Data & backups

| Volume | Contents |
|---|---|
| `pgdata` | the database – **back this up** |
| `keys` | login-cookie encryption keys (losing them only logs everyone out) |
| `caddy_data` | HTTPS certificates (Caddy option) |

```bash
# backup
docker compose -f docker-compose.prod.yml exec -T db pg_dump -U rekrutime -Fc rekrutime > rekrutime-$(date +%Y%m%d).dump
# restore
docker compose -f docker-compose.prod.yml exec -T db pg_restore -U rekrutime -d rekrutime --clean < rekrutime-YYYYMMDD.dump
```
