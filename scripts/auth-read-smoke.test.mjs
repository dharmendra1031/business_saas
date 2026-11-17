import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { createServer } from 'node:http'
import { promisify } from 'node:util'
import { after, test } from 'node:test'

const exec = promisify(execFile)
const requests = []
const server = createServer(async (req, res) => {
  requests.push({ method: req.method, url: req.url })
  if (req.method === 'GET' && ['/api/health', '/api/ready', '/api/auth/options'].includes(req.url)) {
    res.setHeader('Content-Type', 'application/json')
    res.setHeader('Cache-Control', 'no-store')
    res.setHeader('X-Content-Type-Options', 'nosniff')
    res.setHeader('Referrer-Policy', 'no-referrer')
    res.end(JSON.stringify(req.url === '/api/auth/options' ? { canRegister: false }
      : { status: req.url === '/api/health' ? 'ok' : 'ready' }))
    return
  }
  if (req.url === '/api/auth/csrf' && req.method === 'GET') {
    res.setHeader('Set-Cookie', 'test-csrf=first; Path=/; HttpOnly')
    res.setHeader('Content-Type', 'application/json')
    res.end(JSON.stringify({ token: 'test-token' }))
    return
  }
  if (req.url === '/api/auth/login' && req.method === 'POST') {
    const chunks = []
    for await (const chunk of req) chunks.push(chunk)
    const body = JSON.parse(Buffer.concat(chunks).toString())
    if (req.headers['x-csrf-token'] !== 'test-token' ||
        !req.headers.cookie?.includes('test-csrf=first') ||
        body.email !== 'staging@example.test' || body.password !== 'test-password') {
      res.writeHead(403).end()
      return
    }
    res.setHeader('Set-Cookie', 'BusinessSaas.Auth=test-session; Path=/; HttpOnly')
    res.writeHead(204).end()
    return
  }
  if (req.method === 'GET' &&
      ['/api/businesses/7/dashboard', '/api/businesses/7/products?page=1'].includes(req.url) &&
      req.headers.cookie?.includes('BusinessSaas.Auth=test-session')) {
    res.setHeader('Content-Type', 'application/json')
    res.end(JSON.stringify({ items: [] }))
    return
  }
  res.writeHead(404).end()
})

await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
after(() => server.close())

const target = `http://127.0.0.1:${server.address().port}`
const script = new URL('./auth-read-smoke.mjs', import.meta.url).pathname
const env = {
  ...process.env,
  BS_SMOKE_EMAIL: 'staging@example.test',
  BS_SMOKE_PASSWORD: 'test-password',
  BS_SMOKE_BUSINESS_ID: '7'
}

test('reads products with a single login and no business writes', async () => {
  requests.length = 0
  const { stdout } = await exec(process.execPath, [script, target, '8', '2', 'products'], { env })
  assert.match(stdout, /Requests: 8, concurrency: 2, failures: 0/)
  assert.equal(requests.filter(x => x.method === 'POST').length, 1)
  assert.equal(requests.filter(x => x.url === '/api/businesses/7/products?page=1').length, 9)
  assert.equal(requests.filter(x => x.url === '/api/auth/csrf').length, 1)
})

test('rejects remote HTTP and invalid numeric business ID', async () => {
  await assert.rejects(
    exec(process.execPath, [script, 'http://example.com'], { env }),
    /Use a local API URL or explicitly allow an HTTPS staging host/
  )
  await assert.rejects(
    exec(process.execPath, [script, target], {
      env: { ...env, BS_SMOKE_BUSINESS_ID: '7/other' }
    }),
    /numeric BS_SMOKE_BUSINESS_ID/
  )
})

test('staging preflight validates health and production guards', async () => {
  const check = new URL('./staging-preflight.mjs', import.meta.url).pathname
  const { stdout } = await exec(process.execPath, [check, target], { env })
  assert.match(stdout, /Staging preflight passed/)
})
