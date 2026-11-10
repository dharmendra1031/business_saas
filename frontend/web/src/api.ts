export interface User {
  id: number
  email: string
}

export interface BusinessType {
  id: number
  code: string
  name: string
}

export interface BusinessMarket {
  code: string
  name: string
  currencyCode: string
  timeZoneId: string
}

export interface Business {
  id: number
  name: string
  typeCode: string
  typeName: string
  roleCode: string
  countryCode: string
  currencyCode: string
  timeZoneId: string
}

export interface RetailDashboard {
  business: {
    id: number
    name: string
    typeCode: string
    typeName: string
    currencyCode: string
    countryCode: string
    timeZoneId: string
  }
  locations: { id: number; name: string; code: string }[]
}

let csrfToken: string | null = null

export function resetCsrf(): void {
  csrfToken = null
}

async function csrf(): Promise<string> {
  if (csrfToken) return csrfToken

  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
  if (!response.ok) throw new Error('Unable to initialize a secure session.')
  const body = (await response.json()) as { token: string }
  csrfToken = body.token
  return csrfToken
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)
  if (method !== 'GET' && method !== 'HEAD') {
    headers.set('X-CSRF-TOKEN', await csrf())
    if (options.body) headers.set('Content-Type', 'application/json')
  }

  const response = await fetch('/api' + path, {
    ...options,
    method,
    credentials: 'same-origin',
    headers
  })

  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { message?: string; detail?: string }
    throw new Error(body.message ?? body.detail ??
      (response.status === 401 ? 'Please sign in to continue.' : 'Request failed.'))
  }

  return response.status === 204 ? undefined as T : await response.json() as T
}

export function post<T>(path: string, data: unknown): Promise<T> {
  return api<T>(path, { method: 'POST', body: JSON.stringify(data) })
}
