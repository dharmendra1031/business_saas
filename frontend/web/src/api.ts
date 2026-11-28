export interface User {
  id: number
  email: string
  isPlatformOwner: boolean
  mfaVerified: boolean
  globalCatalogueEnabled?: boolean
}

export interface BusinessType {
  id: number
  code: string
  name: string
}

export interface BusinessMarket {
  code: string
  name: string
  currencies: string[]
  timeZones: string[]
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

export interface StaffMember {
  userId: number
  email: string
  roleCode: 'Owner' | 'Manager' | 'Cashier'
}

export interface RetailDashboard {
  roleCode: 'Owner' | 'Manager' | 'Cashier'
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
  subscription: {
    planCode: string
    status: string
    trialEndsAtUtc: string
    paidStartsAtUtc: string | null
    paidEndsAtUtc: string | null
    paidPlanVersionId: number | null
  } | null
  accessStatus: 'Trial' | 'TrialExpired' | 'Active' | 'Inactive' | 'Suspended'
  hasProducts: boolean
  hasInitializedStock?: boolean
  pendingApprovals: number
  stockAlerts?: {
    count: number
    outOfStockCount: number
    items: {
      productId: number
      productName: string
      sku: string
      locationId: number
      locationName: string
      quantity: number
      reorderLevel: number
    }[]
  } | null
  cashSalesToday?: {
    total: number
    billCount: number
    businessDate: string
    recentReceipts: {
      id: number
      locationId: number
      total: number
      postedAtUtc: string
    }[]
  } | null
  cashSaleEnabled?: boolean
  cashRegisterOpeningEnabled?: boolean
  cashRegisterRequired?: boolean
  cashPaymentEnabled?: boolean
  creditPaymentEnabled?: boolean
  receiptAutoOpen?: boolean
  canOperate: boolean
}

let csrfToken: string | null = null
let csrfRequest: Promise<string> | null = null
let authGeneration = 0
let sessionExpiredHandler: (() => void) | null = null

export class ApiResponseError extends Error {
  readonly status: number
  constructor(status: number, message: string, readonly code?: string, readonly productId?: number) {
    super(message)
    this.status = status
  }
}

export class SessionExpiredError extends Error {
  constructor() {
    super('Your session expired. Please sign in again.')
  }
}

// A dropped response to a write does not prove the write failed.
// Callers must check the record or retry with their original operation key.
export class UncertainWriteError extends Error {
  constructor(message = 'Connection lost. The action may have completed. Check the record before retrying.') {
    super(message)
  }
}

export function setSessionExpiredHandler(handler: (() => void) | null): void {
  sessionExpiredHandler = handler
}

// Client operation tokens are not database IDs. Entity IDs remain numeric identities.
export function operationKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
}

export function resetCsrf(): void {
  csrfToken = null
  csrfRequest = null
  authGeneration++
}

function csrf(): Promise<string> {
  if (csrfToken) return Promise.resolve(csrfToken)
  if (csrfRequest) return csrfRequest

  const generation = authGeneration
  csrfRequest = (async () => {
    try {
      let response: Response
      try {
        response = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
      } catch {
        throw new Error('Unable to connect. Check your internet connection.')
      }
      if (!response.ok) throw new Error('Unable to initialize a secure session.')
      const body = (await response.json()) as { token: string }
      if (generation !== authGeneration) throw new SessionExpiredError()
      csrfToken = body.token
      return body.token
    } finally {
      if (generation === authGeneration) csrfRequest = null
    }
  })()
  return csrfRequest
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const requestGeneration = authGeneration
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)
  if (method !== 'GET' && method !== 'HEAD') {
    headers.set('X-CSRF-TOKEN', await csrf())
    if (options.body) headers.set('Content-Type', 'application/json')
  }

  let response: Response
  try {
    response = await fetch('/api' + path, {
      ...options,
      method,
      credentials: 'same-origin',
      headers
    })
  } catch {
    if (requestGeneration !== authGeneration) throw new SessionExpiredError()
    if (method === 'GET' || method === 'HEAD')
      throw new Error('Connection lost. Check your internet connection and try again.')
    throw new UncertainWriteError()
  }

  // Never deliver an earlier account's response after a login or logout.
  if (requestGeneration !== authGeneration) throw new SessionExpiredError()

  if (!response.ok) {
    if (response.status === 401 && !['/auth/login', '/auth/me', '/auth/mfa/verify', '/auth/mfa/setup', '/auth/mfa/enable'].includes(path)) {
      // A response from the previous login must not expire a newer session.
      if (requestGeneration === authGeneration) {
        resetCsrf()
        sessionExpiredHandler?.()
      }
      throw new SessionExpiredError()
    }

    if (response.status === 401 && path === '/auth/login')
      throw new Error('Incorrect email or password.')
    if (response.status === 401 && path === '/auth/mfa/verify')
      throw new Error('Invalid code or expired verification session. Please sign in again if needed.')

    const body = await response.json().catch(() => ({})) as {
      message?: string; detail?: string; code?: string; productId?: number
    }
    throw new ApiResponseError(response.status, body.message ?? body.detail ??
      (response.status === 429 ? 'Too many requests. Wait a minute and try again.'
        : response.status === 401 ? 'Please sign in to continue.' : 'Request failed.'), body.code, body.productId)
  }

  if (response.status === 204) return undefined as T
  try {
    const data = await response.json() as T
    if (requestGeneration !== authGeneration) throw new SessionExpiredError()
    return data
  } catch (cause) {
    if (cause instanceof SessionExpiredError) throw cause
    if (method === 'GET' || method === 'HEAD')
      throw new Error('The server returned an incomplete response. Please retry.')
    throw new UncertainWriteError('The server response was incomplete. Check the record before retrying.')
  }
}

export function post<T>(path: string, data: unknown): Promise<T> {
  return api<T>(path, { method: 'POST', body: JSON.stringify(data) })
}
