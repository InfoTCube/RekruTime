export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// Same-origin in prod, Vite proxy in dev, so paths are always relative ("/api/...").
export async function api<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T> {
  const { json, ...rest } = init ?? {}
  const res = await fetch(path, {
    ...rest,
    credentials: 'include',
    headers: json === undefined ? rest.headers : { 'Content-Type': 'application/json', ...rest.headers },
    body: json === undefined ? rest.body : JSON.stringify(json),
  })
  if (!res.ok) throw new ApiError(res.status, (await res.text()) || res.statusText)
  return res.status === 204 ? (undefined as T) : res.json()
}
