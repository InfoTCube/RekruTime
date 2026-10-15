// CSV (RFC 4180: quoted fields, "" escapes, line breaks inside quotes) or a paste from Google Sheets (tabs).
// Delimiter: tab if present, else whichever of , and ; appears more often outside quotes in the first line.
export function parseTable(text: string): string[][] {
  text = text.replace(/^﻿/, '') // BOM from Excel / Forms export
  const header = firstLine(text)
  const delimiter = text.includes('\t') ? '\t' : countOutsideQuotes(header, ';') > countOutsideQuotes(header, ',') ? ';' : ','

  const rows: string[][] = []
  let row: string[] = []
  let field = ''
  let inQuotes = false
  const endField = () => {
    row.push(field)
    field = ''
  }
  const endRow = () => {
    endField()
    rows.push(row)
    row = []
  }

  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (inQuotes) {
      if (c === '"' && text[i + 1] === '"') {
        field += '"'
        i++
      } else if (c === '"') inQuotes = false
      else field += c
    } else if (c === '"' && field === '') inQuotes = true
    else if (c === delimiter) endField()
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++
      endRow()
    } else field += c
  }
  if (field !== '' || row.length) endRow()
  return rows.map((r) => r.map((f) => f.trim())).filter((r) => r.some((f) => f !== ''))
}

function firstLine(text: string) {
  let inQuotes = false
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '"') inQuotes = !inQuotes
    else if (!inQuotes && (text[i] === '\n' || text[i] === '\r')) return text.slice(0, i)
  }
  return text
}

function countOutsideQuotes(line: string, ch: string) {
  let n = 0
  let inQuotes = false
  for (const c of line) {
    if (c === '"') inQuotes = !inQuotes
    else if (c === ch && !inQuotes) n++
  }
  return n
}
