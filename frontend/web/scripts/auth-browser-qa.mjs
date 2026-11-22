import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { chromium } from 'playwright'

const base = 'http://127.0.0.1:5173'
const output = new URL('../qa-artifacts/', import.meta.url)
await mkdir(output, { recursive: true })

const business = role => ({
  id: 1, name: 'Sample Retail', typeCode: 'RETAIL', typeName: 'Retail',
  countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata', roleCode: role
})
const dashboard = role => ({
  business: {
    id: 1, name: 'Sample Retail', typeCode: 'RETAIL', typeName: 'Retail',
    countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata'
  },
  roleCode: role, locations: [{ id: 1, name: 'Main Shop', code: 'MAIN' }],
  subscription: null, accessStatus: 'Trial', canOperate: true,
  hasProducts: false, pendingApprovals: 0, cashSaleEnabled: role === 'Cashier'
})
const overview = {
  totalCustomers: 0, totalBusinesses: 0, activeSubscriptions: 0, activeTrials: 0,
  expiredTrials: 0, businessGrowth: [], businessesByPlan: [], unassignedBusinesses: 0,
  expiringTrialCount: 0, expiringTrials: [], recentActivity: []
}

function response(route, status, data) {
  return route.fulfill({
    status,
    ...(status === 204 ? { body: '' } : { contentType: 'application/json', body: JSON.stringify(data) })
  })
}

const browser = await chromium.launch({ headless: true })

async function scenario(name, {
  width = 1366, height = 768, role = 'Owner', initialFailure = false,
  businessFailure = false, multiple = false, challenge = false, verified = true
} = {}) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const state = { authenticated: false, initialFailure, businessFailure, challengePending: false }
  let businessRequests = 0

  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname.replace(/^\/api/, '')
    if (path === '/auth/me') {
      if (state.initialFailure) return response(route, 503, { message: 'Temporary authentication service outage' })
      if (!state.authenticated) return response(route, 401, { message: 'Unauthenticated' })
      return response(route, 200, {
        id: 10, email: 'qa@example.test',
        isPlatformOwner: role === 'Platform', mfaVerified: role === 'Platform' && verified
      })
    }
    if (path === '/auth/options') return response(route, 200, { canRegister: false })
    if (path === '/auth/csrf') return response(route, 200, { token: 'qa-test-csrf' })
    if (path === '/auth/login') {
      if (challenge) {
        state.challengePending = true
        return response(route, 200, { requiresTwoFactor: true })
      }
      state.authenticated = true
      return response(route, 204)
    }
    if (path === '/auth/mfa/verify') {
      assert.ok(state.challengePending, 'MFA must follow the password challenge')
      state.authenticated = true
      state.challengePending = false
      return response(route, 204)
    }
    if (path === '/auth/mfa/status') return response(route, 200, { enabled: false, verified: false })
    if (path === '/businesses') {
      businessRequests++
      if (state.businessFailure) {
        state.businessFailure = false
        return response(route, 503, { message: 'Temporary business service outage' })
      }
      const first = business(role)
      return response(route, 200, multiple ? [first, { ...first, id: 2, name: 'Second Retail' }] : [first])
    }
    if (/^\/businesses\/\d+\/dashboard$/.test(path)) return response(route, 200, dashboard(role))
    if (/^\/businesses\/\d+\/cash-sales\/products$/.test(path)) return response(route, 200, [])
    if (path === '/platform/overview') return response(route, 200, overview)
    if (path === '/platform/businesses') return response(route, 200, { items: [], total: 0 })
    if (path === '/platform/plans') return response(route, 200, [])
    if (path === '/platform/access') return response(route, 200, { canViewBusinesses: role === 'Platform' })
    return response(route, 404, { message: 'Not part of this isolated browser fixture' })
  })
  await page.goto(base, { waitUntil: 'domcontentloaded' })

  async function screenshot(file) {
    await page.screenshot({ path: new URL(file + '.png', output).pathname, fullPage: true })
  }
  async function check() {
    assert.deepEqual(errors, [], name + ': uncaught browser errors')
  }
  async function login(viaKeyboard = false) {
    await page.getByRole('heading', { name: 'Sign in' }).waitFor()
    await page.getByLabel('Email address').fill('qa@example.test')
    await page.getByLabel('Password').fill('TestPassword!234')
    if (viaKeyboard) await page.getByLabel('Password').press('Enter')
    else await page.getByRole('button', { name: /^Sign in/ }).click()
  }
  return { page, context, state, screenshot, check, login, get businessRequests() { return businessRequests } }
}

try {
  for (const viewport of [
    { name: '1536-desktop', width: 1536, height: 864 },
    { name: '1366-desktop', width: 1366, height: 768 },
    { name: '768-tablet', width: 768, height: 1024 },
    { name: '360-mobile', width: 360, height: 780 }
  ]) {
    const t = await scenario(viewport.name, viewport)
    await t.page.getByRole('heading', { name: 'Sign in' }).waitFor()
    await t.page.locator('#email').focus()
    await t.page.keyboard.press('Tab')
    assert.equal(await t.page.evaluate(() => document.activeElement?.id), 'password',
      viewport.name + ': Tab should focus password')
    await t.page.keyboard.press('Tab')
    assert.equal(await t.page.evaluate(() => document.activeElement?.getAttribute('type')), 'submit',
      viewport.name + ': Tab should focus Sign in')
    const width = await t.page.evaluate(() => document.documentElement.scrollWidth)
    assert.ok(width <= viewport.width + 1, viewport.name + ': horizontal overflow ' + width)
    await t.screenshot('login-' + viewport.name)
    await t.check()
    await t.context.close()
    console.log('PASS responsive login', viewport.name)
  }

  {
    const t = await scenario('initial-outage', { initialFailure: true })
    await t.page.getByRole('heading', { name: 'Unable to load your session' }).waitFor()
    await t.screenshot('initial-session-outage')
    t.state.initialFailure = false
    await t.page.getByRole('button', { name: 'Retry connection' }).click()
    await t.page.getByRole('heading', { name: 'Sign in' }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS session outage / retry / unauthenticated')
  }

  {
    const t = await scenario('business-outage', { businessFailure: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Unable to load businesses' }).waitFor()
    assert.equal(await t.page.getByText('Temporary business service outage').count(), 1)
    await t.screenshot('assigned-business-outage')
    await t.page.getByRole('button', { name: 'Retry loading businesses' }).click()
    await t.page.getByRole('heading', { name: 'Welcome to Sample Retail' }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS authenticated business outage / retry / direct workspace')
  }

  {
    const t = await scenario('business-owner', { role: 'Owner' })
    await t.login(true)
    await t.page.getByRole('heading', { name: 'Welcome to Sample Retail' }).waitFor()
    await t.screenshot('owner-workspace')
    await t.check()
    await t.context.close()
    console.log('PASS owner automatic destination')
  }

  {
    const t = await scenario('multi-business', { role: 'Manager', multiple: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'My Assigned Businesses' }).waitFor()
    assert.equal(await t.page.locator('.business-row').count(), 2)
    await t.screenshot('multi-business-picker')
    await t.page.locator('.business-row').first().click()
    await t.page.getByRole('heading', { name: 'Welcome to Sample Retail' }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS multi-business select / manager workspace')
  }

  {
    const t = await scenario('cashier', { role: 'Cashier' })
    await t.login()
    await t.page.locator('.pos-shell').waitFor()
    await t.page.getByText('Loading products…').waitFor({ state: 'hidden' })
    await t.screenshot('cashier-pos-workspace')
    assert.equal(await t.page.locator('.app-sidebar').count(), 0,
      'cashier POS must not show business/admin sidebar')
    await t.check()
    await t.context.close()
    console.log('PASS cashier direct POS / no admin sidebar')
  }

  {
    const t = await scenario('platform', { role: 'Platform' })
    await t.login()
    await t.page.locator('.platform-shell').waitFor()
    await t.page.getByText('SUPER ADMIN MODULES').waitFor()
    await t.screenshot('platform-super-admin-workspace')
    await t.check()
    await t.context.close()
    console.log('PASS Super Admin destination')
  }

  {
    const t = await scenario('platform-mfa-setup', { role: 'Platform', verified: false })
    await t.login()
    await t.page.getByRole('heading', { name: 'Secure your Super Admin account' }).waitFor()
    await t.screenshot('platform-mfa-setup')
    await t.check()
    await t.context.close()
    console.log('PASS Super Admin MFA setup required')
  }

  {
    const t = await scenario('platform-mfa-challenge', { role: 'Platform', challenge: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Verify your sign in' }).waitFor()
    await t.page.getByLabel('6-digit authenticator code').fill('123456')
    await t.page.getByRole('button', { name: 'Verify and sign in' }).click()
    await t.page.locator('.platform-shell').waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS Super Admin MFA challenge')
  }

  console.log('PASS all first-screen browser QA scenarios')
} finally {
  await browser.close()
}
