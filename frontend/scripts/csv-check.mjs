
// Run: npm test  (Node 24 runs TypeScript directly)
const { parseTable } = await import('../src/csv.ts')
const eq = (a, b, name) => { const ok = JSON.stringify(a) === JSON.stringify(b); console.log(ok ? 'ok  ' : 'FAIL', name, ok ? '' : JSON.stringify(a)); if (!ok) process.exitCode = 1 }
// Google Forms CSV export: BOM, quoted header with a comma, multi-answer cell, CRLF, "" escape, line break inside quotes
eq(parseTable('﻿"Sygnatura czasowa","Imię i nazwisko","Sekcje (wybierz, ile chcesz)"\r\n"2026/10/01","Jan ""Johnny"" Kowalski","AI, Web"\r\n"2026/10/02","Anna\nNowak","Bio"\r\n'),
  [['Sygnatura czasowa', 'Imię i nazwisko', 'Sekcje (wybierz, ile chcesz)'], ['2026/10/01', 'Jan "Johnny" Kowalski', 'AI, Web'], ['2026/10/02', 'Anna\nNowak', 'Bio']], 'forms csv')
eq(parseTable('Imię\tE-mail\tSekcje\nJan\tjan@x.pl\tAI, Web\n\n'), [['Imię', 'E-mail', 'Sekcje'], ['Jan', 'jan@x.pl', 'AI, Web']], 'sheets paste (tabs, blank line)')
eq(parseTable('Imię;E-mail;Sekcje\nJan;jan@x.pl;"AI; Web"'), [['Imię', 'E-mail', 'Sekcje'], ['Jan', 'jan@x.pl', 'AI; Web']], 'excel ; csv, no trailing newline')
eq(parseTable('a,b\n, \n'), [['a', 'b']], 'drops empty rows')
