// Starts the app for the browser tests, like production: the built frontend served by the API (one origin).
// Every run starts from an empty database (rekrutime_e2e – never the dev one) on the Postgres from docker-compose.yml
// (CI: a service container). E-mails go to Mailpit.
import { spawn, spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import pg from 'pg'
import { config } from './config.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

// 1. Empty database
const admin = new pg.Client({ ...config.db, database: 'postgres' })
await admin.connect()
await admin.query(`DROP DATABASE IF EXISTS ${config.db.database} WITH (FORCE)`)
await admin.query(`CREATE DATABASE ${config.db.database}`)
await admin.end()

// 2. Current frontend build
if (!process.env.E2E_SKIP_BUILD) {
  const build = spawnSync('npm run build', { cwd: path.join(root, 'frontend'), stdio: 'inherit', shell: true })
  if (build.status !== 0) process.exit(build.status ?? 1)
}

// 3. API, built into its own folder (a running dev API locks backend/…/bin), then started from there – the folder is
// its content root, so it reads the appsettings.json next to it. Migrates the empty DB and seeds the admin on startup.
const out = path.join(root, 'e2e', '.api')
const dotnetBuild = spawnSync('dotnet', ['build', path.join(root, 'backend', 'RekruTime.Api'), '-o', out, '--nologo', '-v', 'q'], { stdio: 'inherit' })
if (dotnetBuild.status !== 0) process.exit(dotnetBuild.status ?? 1)

const { db } = config
const api = spawn('dotnet', [path.join(out, 'RekruTime.Api.dll'), '--urls', config.baseURL], {
  cwd: out,
  stdio: 'inherit',
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'E2E',
    ASPNETCORE_WEBROOT: path.join(root, 'frontend', 'dist'),
    ConnectionStrings__Default: `Host=${db.host};Port=${db.port};Database=${db.database};Username=${db.user};Password=${db.password}`,
    SeedAdmin__Email: config.admin.email,
    SeedAdmin__Password: config.admin.password,
    SeedAdmin__DisplayName: 'E2E Admin',
    Club__Name: 'Koło Testowe',
    Club__PublicUrl: config.baseURL,
    Club__ContactEmail: 'rekrutacja@e2e.local',
    Club__InterviewLocation: 'Sala 101',
    Email__Host: config.mailpit.smtpHost,
    Email__Port: String(config.mailpit.smtpPort),
    Email__Security: 'None',
    Email__FromAddress: 'rekrutacja@e2e.local',
    Email__DelayBetweenMessagesMs: '0',
  },
})
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => (api.kill(signal), process.exit()))
api.on('exit', (code) => process.exit(code ?? 0))
