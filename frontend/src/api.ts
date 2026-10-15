type Problem = { title?: string; detail?: string; errors?: Record<string, string[]> }

export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string[]> // validation errors keyed by field, e.g. { Name: [...] }

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
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
  if (!res.ok) {
    // Backend errors are ProblemDetails JSON; anything else falls back to the status text.
    const problem: Problem = await res.json().catch(() => ({}))
    const firstFieldError = Object.values(problem.errors ?? {})[0]?.[0]
    throw new ApiError(res.status, problem.detail ?? firstFieldError ?? problem.title ?? res.statusText, problem.errors)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')

// Validation message for one field (backend uses PascalCase names), for Mantine's `error` prop.
export const fieldError = (e: unknown, field: string) => (e instanceof ApiError ? e.fieldErrors[field]?.[0] : undefined)
