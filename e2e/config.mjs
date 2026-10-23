// Where the browser tests find the app, its database and Mailpit. Defaults = `docker compose up -d` from the repo root;
// CI overrides them with environment variables.
const env = process.env

export const config = {
  baseURL: env.E2E_URL ?? 'http://localhost:5090', // not 5080 – the dev API can keep running
  db: {
    host: env.E2E_DB_HOST ?? 'localhost',
    port: Number(env.E2E_DB_PORT ?? 5440),
    user: env.E2E_DB_USER ?? 'rekrutime',
    password: env.E2E_DB_PASSWORD ?? 'rekrutime',
    database: 'rekrutime_e2e',
  },
  mailpit: {
    smtpHost: env.E2E_SMTP_HOST ?? 'localhost',
    smtpPort: Number(env.E2E_SMTP_PORT ?? 1025),
    api: env.E2E_MAILPIT_API ?? 'http://localhost:8025/api/v1',
  },
  admin: { email: 'admin@e2e.local', password: 'Admin123!' },
}
