// Read-only authenticated Retail baseline for a disposable staging business.
const base = new URL(process.argv[2] ?? 'http://127.0.0.1:5080')
const count = Number(process.argv[3] ?? 30)
const concurrency = Number(process.argv[4] ?? 3)
const resource = process.argv[5] ?? 'dashboard'
const businessId = Number(process.env.BS_SMOKE_BUSINESS_ID)
const email = process.env.BS_SMOKE_EMAIL?.trim()
const password = process.env.BS_SMOKE_PASSWORD

const local = ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname)
if (!['http:', 'https:'].includes(base.protocol) ||
    base.pathname !== '/' || base.search || base.hash || base.username || base.password ||
    (!local && (base.protocol !== 'https:' || process.env.BS_SMOKE_REMOTE_STAGING !== 'yes'))) {
  throw new Error('Use a local API URL or explicitly allow an HTTPS staging host.')
}
if (!Number.isSafeInteger(businessId) || businessId <= 0 || !email || !password ||
    !Number.isInteger(count) || count < 1 || count > 200 ||
    !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 10 ||
    !['dashboard', 'products'].includes(resource)) {
  throw new Error('Set BS_SMOKE_EMAIL, BS_SMOKE_PASSWORD and numeric BS_SMOKE_BUSINESS_ID; usage: node scripts/auth-read-smoke.mjs [base-url] [1-200 requests] [1-10 concurrency] [dashboard|products]')
}

const cookies = new Map()
function collectCookies(response) {
  for (const value of response.headers.getSetCookie()) {
    const pair = value.split(';', 1)[0]
    const separator = pair.indexOf('=')
    if (separator > 0) cookies.set(pair.slice(0, separator), pair.slice(separator + 1))
  }
}
function cookieHeader() {
  return [...cookies].map(([name, value]) => `${name}=${value}`).join('; ')
}
const requestUrl = path => new URL(path, base)
const csrfResponse = await fetch(requestUrl('/api/auth/csrf'), {
  redirect: 'error', signal: AbortSignal.timeout(10000)
})
if (!csrfResponse.ok) throw new Error(`CSRF setup failed: HTTP ${csrfResponse.status}`)
collectCookies(csrfResponse)
const { token } = await csrfResponse.json()
if (typeof token !== 'string' || !token) throw new Error('CSRF token is missing.')

const loginResponse = await fetch(requestUrl('/api/auth/login'), {
  method: 'POST',
  redirect: 'error',
  signal: AbortSignal.timeout(10000),
  headers: {
    'Content-Type': 'application/json',
    'X-CSRF-TOKEN': token,
    Cookie: cookieHeader()
  },
  body: JSON.stringify({ email, password })
})
if (loginResponse.status !== 204) throw new Error(`Staging login failed: HTTP ${loginResponse.status}`)
collectCookies(loginResponse)

const path = `/api/businesses/${businessId}/${resource === 'products' ? 'products?page=1' : 'dashboard'}`
const headers = { Cookie: cookieHeader() }
if (!headers.Cookie) throw new Error('No session cookie was returned.')

async function read() {
  return fetch(requestUrl(path), {
    headers, redirect: 'error', signal: AbortSignal.timeout(10000)
  })
}

const check = await read()
if (check.status !== 200) throw new Error(`Read access check failed: HTTP ${check.status}`)
await check.arrayBuffer()

let next = 0
let failures = 0
const timings = []
const started = performance.now()
async function worker() {
  while (next < count) {
    next += 1
    const before = performance.now()
    try {
      const response = await read()
      await response.arrayBuffer()
      if (response.status !== 200) failures += 1
    } catch {
      failures += 1
    }
    timings.push(performance.now() - before)
  }
}
await Promise.all(Array.from({ length: Math.min(count, concurrency) }, worker))
timings.sort((a, b) => a - b)
const percentile = fraction => timings[Math.ceil(count * fraction) - 1].toFixed(1)
const elapsed = (performance.now() - started) / 1000
console.log(`Read-only ${resource} baseline: ${base.origin}`)
console.log(`Requests: ${count}, concurrency: ${concurrency}, failures: ${failures}`)
console.log(`p50: ${percentile(0.5)} ms, p95: ${percentile(0.95)} ms, throughput: ${(count / elapsed).toFixed(1)} req/s`)
if (failures) process.exitCode = 1
