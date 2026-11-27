import { toast } from 'sonner'

export type Problem = { title?: string; detail?: string; errors?: { elementId: string | null; reason: string }[] }

export class ApiError extends Error {
  status: number
  problem: Problem
  constructor(status: number, problem: Problem) {
    super(problem.title ?? `HTTP ${status}`)
    this.status = status
    this.problem = problem
  }
}

// The session sets this to switch the app to the logged-out state on any 401.
let onUnauthorized: () => void = () => {}
export const setOnUnauthorized = (fn: () => void) => { onUnauthorized = fn }

export async function api<T = undefined>(
  method: string, path: string, opts: { body?: unknown; rawBody?: string; etag?: string } = {},
): Promise<{ data: T; etag: string | null }> {
  const headers: Record<string, string> = { 'X-CSRF': '1' }
  if (opts.etag) headers['If-Match'] = opts.etag
  let body: string | undefined
  if (opts.rawBody !== undefined) { body = opts.rawBody; headers['Content-Type'] = 'application/geo+json' }
  else if (opts.body !== undefined) { body = JSON.stringify(opts.body); headers['Content-Type'] = 'application/json' }
  const res = await fetch(`/api/v1${path}`, { method, headers, body, credentials: 'same-origin' })
  if (res.status === 401) onUnauthorized()
  if (!res.ok) {
    const problem: Problem = await res.json().catch(() => ({}))
    throw new ApiError(res.status, problem)
  }
  const text = await res.text()
  return { data: (text ? JSON.parse(text) : undefined) as T, etag: res.headers.get('ETag') }
}

export function toastError(e: unknown) {
  if (e instanceof ApiError) {
    if (e.status === 401) return
    if (e.status === 412) return toast.error('Changed by someone else — reload')
    return toast.error(e.problem.title ?? `Error ${e.status}`, { description: e.problem.detail })
  }
  toast.error('Request failed', { description: String(e) })
}

export type Navigation = {
  walkingSpeedMps: number; unpavedSpeedFactor: number; stepsSpeedFactor: number
  connectToleranceM: number; maxSnapDistanceM: number
}
export type Site = { siteId: string; name: string; timeZone: string; navigation: Navigation }
export type Device = { deviceId: string; siteId: string }
