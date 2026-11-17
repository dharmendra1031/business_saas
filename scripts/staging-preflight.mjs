// Read-only deployment checks for a non-development staging instance.
const base = new URL(process.argv[2] ?? 'http://127.0.0.1:5080')
const local = ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname)
if (!['http:', 'https:'].includes(base.protocol) ||
    base.pathname !== '/' || base.search || base.hash || base.username || base.password ||
    (!local && (base.protocol !== 'https:' || process.env.BS_SMOKE_REMOTE_STAGING !== 'yes'))) {
  throw new Error('Use a local API URL or explicitly allow an HTTPS staging host.')
}

const checks = [
  ['/api/health', 'ok'],
  ['/api/ready', 'ready']
]
for (const [path, status] of checks) {
  const response = await fetch(new URL(path, base), {
    redirect: 'error', signal: AbortSignal.timeout(10000)
  })
  if (!response.ok) throw new Error(`${path} returned HTTP ${response.status}`)
  if ((await response.json()).status !== status)
    throw new Error(`${path} returned an unexpected status`)
  if (!response.headers.get('cache-control')?.includes('no-store') ||
      response.headers.get('x-content-type-options') !== 'nosniff' ||
      response.headers.get('referrer-policy') !== 'no-referrer') {
    throw new Error(`${path} is missing API security headers`)
  }
}

const options = await fetch(new URL('/api/auth/options', base), {
  redirect: 'error', signal: AbortSignal.timeout(10000)
})
if (!options.ok || (await options.json()).canRegister !== false)
  throw new Error('Production registration must be disabled on staging.')

console.log(`Staging preflight passed for ${base.origin}: health, readiness, headers and registration guard.`)
