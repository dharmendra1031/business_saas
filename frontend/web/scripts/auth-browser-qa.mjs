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
const dashboard = (role, hasProducts = false, pendingApprovals = 0) => ({
  business: {
    id: 1, name: 'Sample Retail', typeCode: 'RETAIL', typeName: 'Retail',
    countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata'
  },
  roleCode: role, locations: [{ id: 1, name: 'Main Shop', code: 'MAIN' }],
  subscription: { planCode: 'STARTER', status: 'Trial',
    trialEndsAtUtc: new Date(Date.now() + 14 * 24 * 60 * 60 * 1000).toISOString(),
    paidStartsAtUtc: null, paidEndsAtUtc: null, paidPlanVersionId: null },
  accessStatus: 'Trial', canOperate: true,
  hasProducts, pendingApprovals, cashSaleEnabled: role === 'Cashier'
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
  businessFailure = false, multiple = false, challenge = false, verified = true,
  badPassword = false, lockedPassword = false, badMfa = false, hasProducts = false, pendingApprovals = 0
} = {}) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const state = { authenticated: false, initialFailure, businessFailure, challengePending: false,
    badPassword, lockedPassword, badMfa, lastMfaRequest: null }
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
      if (state.badPassword) {
        state.badPassword = false
        return response(route, 401, { message: 'Unauthorized' })
      }
      if (state.lockedPassword) {
        state.lockedPassword = false
        return response(route, 429, { detail: 'Account temporarily locked. Try again later.' })
      }
      if (challenge) {
        state.challengePending = true
        return response(route, 200, { requiresTwoFactor: true })
      }
      state.authenticated = true
      return response(route, 204)
    }
    if (path === '/auth/mfa/verify') {
      assert.ok(state.challengePending, 'MFA must follow the password challenge')
      state.lastMfaRequest = route.request().postDataJSON()
      if (state.badMfa) {
        state.badMfa = false
        return response(route, 401, { message: 'Invalid verification code' })
      }
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
    if (/^\/businesses\/\d+\/dashboard$/.test(path)) return response(route, 200, dashboard(role, hasProducts, pendingApprovals))
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
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS authenticated business outage / retry / direct workspace')
  }

  {
    const t = await scenario('business-owner', { role: 'Owner' })
    await t.login(true)
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    const dashboardTop = await t.page.locator('.business-dashboard-summary').boundingBox()
    assert.ok(dashboardTop && dashboardTop.y < 200, 'Dashboard summary must begin near the top')
    assert.equal(await t.page.locator('.business-action-card').count(), 0, 'No duplicated navigation cards')
    await t.page.getByRole('heading', { name: 'Complete store setup' }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').count(), 0, 'New businesses must not show empty KPI tiles')
    assert.equal(await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).count(), 1)
    assert.equal(await t.page.getByText('No plan', { exact: false }).count(), 0,
      'Trial fixture must show an actual plan rather than conflicting No plan status')
    assert.equal(await t.page.getByText('Welcome to Sample Retail').count(), 0)
    assert.equal(await t.page.getByText('Switch Business').count(), 0, 'No single-business switch')
    await t.screenshot('owner-workspace')
    await t.check()
    await t.context.close()
    console.log('PASS owner automatic destination')
  }


  {
    const t = await scenario('business-owner-mobile', { role: 'Owner', width: 360, height: 780 })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    const width = await t.page.evaluate(() => document.documentElement.scrollWidth)
    assert.ok(width <= 361, 'Business dashboard must not overflow 360px mobile')
    const summary = await t.page.locator('.business-dashboard-summary').boundingBox()
    assert.ok(summary && summary.y < 210, 'Mobile summary must be visible without a large hero')
    assert.equal(await t.page.locator('.business-action-card').count(), 0, 'No duplicate menu cards')
    assert.equal(await t.page.locator('.business-kpi-card').count(), 0, 'Mobile new-store view must not show KPI tiles')
    await t.screenshot('BUS-01-owner-dashboard-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 compact business dashboard at 360px')
  }

  {
    const t = await scenario('owner-operational', { role: 'Owner', hasProducts: true, pendingApprovals: 3 })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').count(), 4)
    assert.equal(await t.page.locator('.business-kpi-card').first().locator('strong').innerText(), '—',
      'Never fabricate sales figures before reporting integration')
    await t.page.getByRole('heading', { name: 'Attention Needed' }).waitFor()
    await t.page.getByRole('heading', { name: 'Recent Activity' }).waitFor()
    assert.equal(await t.page.getByRole('heading', { name: 'Complete store setup' }).count(), 0)
    assert.equal(await t.page.locator('.business-action-card').count(), 0)
    await t.screenshot('BUS-01-owner-operational-desktop')
    await t.page.getByRole('button', { name: /Review 3 draft purchase orders/ }).click()
    await t.page.getByRole('heading', { name: 'Purchase Orders' }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 operational owner three sections and review action')
  }

  {
    const t = await scenario('owner-operational-mobile', {
      role: 'Owner', hasProducts: true, pendingApprovals: 1, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Operational overview should fit 360px mobile')
    await t.screenshot('BUS-01-owner-operational-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 operational owner mobile layout')
  }

  {
    const t = await scenario('multi-business', { role: 'Manager', multiple: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'My Assigned Businesses' }).waitFor()
    assert.equal(await t.page.locator('.business-row').count(), 2)
    await t.screenshot('multi-business-picker')
    await t.page.locator('.business-row').first().click()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
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

  {
    const t = await scenario('platform-bad-password', { role: 'Platform', badPassword: true })
    await t.login()
    await t.page.getByRole('alert').getByText('Incorrect email or password.').waitFor()
    assert.equal(await t.page.locator('.platform-shell').count(), 0, 'Failed login cannot enter platform')
    await t.login()
    await t.page.locator('.platform-shell').waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS AUTH-02 invalid password then successful login')
  }

  {
    const t = await scenario('platform-password-lockout', { role: 'Platform', lockedPassword: true })
    await t.login()
    await t.page.getByRole('alert').getByText('Account temporarily locked. Try again later.').waitFor()
    assert.equal(await t.page.locator('.platform-shell').count(), 0)
    await t.check()
    await t.context.close()
    console.log('PASS AUTH-02 rate-limit / lockout response')
  }

  {
    const t = await scenario('platform-invalid-mfa', {
      role: 'Platform', challenge: true, badMfa: true, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Verify your sign in' }).waitFor()
    assert.equal(await t.page.locator('.platform-shell').count(), 0, 'MFA cannot be skipped')
    const input = t.page.getByLabel('6-digit authenticator code')
    await input.fill('abcdef')
    assert.equal(await input.evaluate(element => element.checkValidity()), false,
      'Authenticator challenge must reject malformed text before submission')
    await input.fill('123456')
    await t.screenshot('AUTH-02-mfa-challenge-mobile')
    const width = await t.page.evaluate(() => document.documentElement.scrollWidth)
    assert.ok(width <= 361, 'MFA challenge should fit 360px mobile viewport')
    await t.page.getByRole('button', { name: 'Verify and sign in' }).click()
    await t.page.getByRole('alert').getByText('Invalid code or expired verification session. Please sign in again if needed.').waitFor()
    assert.equal(await t.page.locator('.platform-shell').count(), 0, 'Invalid MFA cannot enter platform')
    await input.fill('654321')
    await t.page.getByRole('button', { name: 'Verify and sign in' }).click()
    await t.page.locator('.platform-shell').waitFor()
    assert.equal(t.state.lastMfaRequest.code, '654321')
    await t.check()
    await t.context.close()
    console.log('PASS AUTH-02 MFA validation, invalid retry, mobile fit and authorization gate')
  }

  {
    const t = await scenario('platform-mfa-reset-and-recovery', { role: 'Platform', challenge: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Verify your sign in' }).waitFor()
    await t.page.getByRole('button', { name: 'Use a recovery code' }).click()
    await t.page.getByLabel('Recovery code').fill('recovery-12345')
    await t.page.getByRole('button', { name: 'Back to password sign in' }).click()
    await t.login()
    await t.page.getByLabel('6-digit authenticator code').waitFor()
    await t.page.getByRole('button', { name: 'Use a recovery code' }).click()
    await t.page.getByLabel('Recovery code').fill('recovery-67890')
    assert.equal(await t.page.getByLabel('Recovery code').getAttribute('type'), 'password',
      'Recovery codes should not be visible on screen by default')
    await t.screenshot('AUTH-02-recovery-code-desktop')
    await t.page.getByRole('button', { name: 'Verify and sign in' }).click()
    await t.page.locator('.platform-shell').waitFor()
    assert.equal(t.state.lastMfaRequest.recoveryCode, 'recovery-67890')
    assert.equal(t.state.lastMfaRequest.code, null)
    await t.check()
    await t.context.close()
    console.log('PASS AUTH-02 recovery code, back and fresh MFA mode')
  }

  console.log('PASS all first-screen browser QA scenarios')
} finally {
  await browser.close()
}
