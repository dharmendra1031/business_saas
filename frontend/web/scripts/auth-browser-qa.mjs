import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from 'playwright'

const base = process.env.BUSINESS_SAAS_QA_URL || 'http://127.0.0.1:5173'
const output = process.env.BUSINESS_SAAS_QA_OUTPUT ||
  fileURLToPath(new URL('../qa-artifacts/', import.meta.url))
await mkdir(output, { recursive: true })

const business = role => ({
  id: 1, name: 'Sample Retail', typeCode: 'RETAIL', typeName: 'Retail',
  countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata', roleCode: role
})
const dashboard = (role, hasProducts = false, pendingApprovals = 0, selectedBusinessId = 1, locationCount = 1, cashSalesFixture = false, stockAlertsFixture = false, ownerPos = false) => ({
  business: {
    id: selectedBusinessId, name: selectedBusinessId === 2 ? 'Second Retail' : 'Sample Retail', typeCode: 'RETAIL', typeName: 'Retail',
    countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata'
  },
  roleCode: role, locations: [
    { id: 1, name: 'Main Shop', code: 'MAIN' },
    ...(locationCount > 1 ? [{ id: 2, name: 'Second Location', code: 'SECOND' }] : [])
  ],
  subscription: { planCode: 'STARTER', status: 'Trial',
    trialEndsAtUtc: new Date(Date.now() + 14 * 24 * 60 * 60 * 1000).toISOString(),
    paidStartsAtUtc: null, paidEndsAtUtc: null, paidPlanVersionId: null },
  accessStatus: 'Trial', canOperate: true,
  hasProducts, pendingApprovals, cashSaleEnabled: role === 'Cashier' || ownerPos,
  stockAlerts: stockAlertsFixture && role === 'Owner' ? {
    count: 2, outOfStockCount: 1,
    items: [
      { productId: 11, productName: 'Blue Shirt', sku: 'BLUE-11',
        locationId: 1, locationName: 'Main Shop', quantity: 0, reorderLevel: 4 },
      { productId: 12, productName: 'Red Bag', sku: 'BAG-12',
        locationId: 2, locationName: 'Second Location', quantity: 2, reorderLevel: 3 }
    ]
  } : null,
  cashSalesToday: cashSalesFixture && role === 'Owner' ? {
    total: 150.25, billCount: 2, businessDate: '2026-11-22',
    recentReceipts: [
      { id: 41, locationId: 1, total: 150.25, postedAtUtc: '2026-11-22T12:00:00Z' }
    ]
  } : null
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
  badPassword = false, lockedPassword = false, badMfa = false, hasProducts = false, pendingApprovals = 0,
  locationCount = 1, productsFixture = false, cashSalesFixture = false, stockAlertsFixture = false, ownerPos = false
} = {}) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const state = { authenticated: false, initialFailure, businessFailure, challengePending: false,
    badPassword, lockedPassword, badMfa, lastMfaRequest: null }
  let businessRequests = 0
  const stockPaths = []
  const stockQueries = []

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
    if (path === '/auth/logout') {
      state.authenticated = false
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
    if (/^\/businesses\/\d+\/dashboard$/.test(path)) return response(route, 200, dashboard(role, hasProducts, pendingApprovals, Number(path.split('/')[2]), locationCount, cashSalesFixture, stockAlertsFixture, ownerPos))
    if (/^\/businesses\/\d+\/purchase-orders$/.test(path) && route.request().method() === 'GET')
      return response(route, 200, { items: [], total: 0, page: 1, pageSize: 20 })
    if (/^\/businesses\/\d+\/suppliers$/.test(path) && route.request().method() === 'GET')
      return response(route, 200, {
        items: [{ id: 21, name: 'Wholesaler', phone: '1234567890', email: '', isActive: true }],
        total: 1, page: 1, pageSize: 20
      })
    if (/^\/businesses\/\d+\/stock\/locations\/\d+\/movements$/.test(path)) {
      if (!stockAlertsFixture) return response(route, 200, { items: [], total: 0, page: 1, pageSize: 20 })
      const now = '2026-11-23T02:00:00Z'
      const items = [
        { id: 5, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
          quantityChange: -4, balanceAfter: 0, sourceType: 'CashSaleLine',
          sourceId: 89, reason: null, postedAtUtc: now, actor: 'cashier@example.test' },
        { id: 4, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
          quantityChange: 4, balanceAfter: 4, sourceType: 'Receiving',
          sourceId: 77, reason: null, postedAtUtc: now, actor: 'owner@example.test' }
      ]
      return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
    }
    if (/^\/businesses\/\d+\/stock\/locations\/\d+$/.test(path)) {
      stockPaths.push(path)
      const url = new URL(route.request().url())
      stockQueries.push(path + url.search)
      if (stockAlertsFixture) {
        const locationId = Number(path.split('/').at(-1))
        const items = locationId === 1
          ? [
              { id: 11, name: 'Blue Shirt', sku: 'BLUE-11', isActive: true,
                quantity: 0, hasStockRecord: true, reorderLevel: 4 },
              { id: 13, name: 'Fine Notebook', sku: 'NOTE-13', isActive: true,
                quantity: 20, hasStockRecord: true, reorderLevel: 5 }
            ]
          : [{ id: 12, name: 'Red Bag', sku: 'BAG-12', isActive: true,
              quantity: 2, hasStockRecord: true, reorderLevel: 3 }]
        const filtered = url.searchParams.get('alertsOnly') === 'true'
          ? items.filter(item => item.reorderLevel !== null && item.quantity <= item.reorderLevel)
          : items
        return response(route, 200, { items: filtered, total: filtered.length, page: 1, pageSize: 20 })
      }
      return response(route, 200, { items: [], total: 0, page: 1, pageSize: 20 })
    }
    if (productsFixture && /^\/businesses\/\d+\/products\/options$/.test(path))
      return response(route, 200, {
        categories: [{ id: 1, name: 'General', parentId: null, isActive: true },
          { id: 2, name: 'Clothing', parentId: null, isActive: true }],
        units: [{ id: 1, name: 'Item', code: 'ITEM' }]
      })
    if (productsFixture && /^\/businesses\/\d+\/products$/.test(path)) {
      const params = new URL(route.request().url()).searchParams
      let items = [
        { id: 11, name: 'Blue Shirt', sku: 'BLUE-11', barcode: '100001', categoryId: 2,
          categoryName: 'Clothing', unitId: 1, unitCode: 'ITEM', salePrice: 450,
          taxClassification: '', reorderLevel: 4, isActive: true },
        { id: 12, name: 'Red Bag', sku: 'BAG-12', barcode: null, categoryId: 1,
          categoryName: 'General', unitId: 1, unitCode: 'ITEM', salePrice: 220,
          taxClassification: '', reorderLevel: null, isActive: false }
      ]
      const term = (params.get('search') ?? '').toLowerCase()
      if (term) items = items.filter(item =>
        item.name.toLowerCase().includes(term) || item.sku.toLowerCase().includes(term) ||
        item.barcode?.includes(term))
      const status = params.get('status')
      if (status === 'active') items = items.filter(item => item.isActive)
      if (status === 'inactive') items = items.filter(item => !item.isActive)
      const category = Number(params.get('categoryId') || 0)
      if (category > 0) items = items.filter(item => item.categoryId === category)
      return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
    }
    if (/^\/businesses\/\d+\/cash-sales\/products$/.test(path)) return response(route, 200, [])
    if (path === '/platform/overview') return response(route, 200, overview)
    if (path === '/platform/businesses') return response(route, 200, { items: [], total: 0 })
    if (path === '/platform/plans') return response(route, 200, [])
    if (path === '/platform/access') return response(route, 200, { canViewBusinesses: role === 'Platform' })
    return response(route, 404, { message: 'Not part of this isolated browser fixture' })
  })
  await page.goto(base, { waitUntil: 'domcontentloaded' })

  async function screenshot(file) {
    await page.screenshot({ path: resolve(output, file + '.png'), fullPage: true })
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
  return { page, context, state, screenshot, check, login, get businessRequests() { return businessRequests }, stockPaths, stockQueries }
}

try {
  {
    // Invitation URLs must not remain in browser history after the app boots.
    const context = await browser.newContext({ viewport: { width: 360, height: 780 } })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    const token = 'A'.repeat(43)
    const expiresAtUtc = new Date(Date.now() + 86400000).toISOString()
    let accepted
    await page.route('**/api/**', route => {
      const path = new URL(route.request().url()).pathname
      if (path === '/api/auth/csrf') return response(route, 200, { token: 'qa-csrf' })
      if (path === '/api/auth/owner-invitations/inspect') {
        assert.equal(route.request().postDataJSON().token, token)
        return response(route, 200, { email: 'invited-owner@example.test', expiresAtUtc })
      }
      if (path === '/api/auth/owner-invitations/accept') {
        accepted = route.request().postDataJSON()
        return response(route, 200, {
          verified: true, email: 'invited-owner@example.test',
          message: 'Account verified. Sign in to continue.'
        })
      }
      if (path === '/api/public/platform-availability')
        return response(route, 200, { maintenance: null, announcements: [] })
      return response(route, 404, {})
    })
    await page.goto(base + '/?invite=' + token)
    await page.getByRole('heading', { name: 'Accept Owner invitation' }).waitFor()
    assert.equal(new URL(page.url()).search, '',
      'Invitation bearer token must not remain in browser history')
    await page.getByText('invited-owner@example.test').waitFor()
    await page.getByLabel('New password').fill('OwnerPassword123!')
    await page.getByLabel('Confirm password').fill('NotTheSamePassword123!')
    assert.equal(await page.getByRole('button', { name: /Verify email & create account/ }).isDisabled(), true)
    await page.getByLabel('Confirm password').fill('OwnerPassword123!')
    await page.getByRole('button', { name: /Verify email & create account/ }).click()
    await page.getByRole('heading', { name: 'Account verified' }).waitFor()
    assert.deepEqual(accepted, { token, password: 'OwnerPassword123!' })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Invitation acceptance must fit 360px mobile without horizontal overflow')
    assert.deepEqual(errors, [], 'Invitation page must not throw browser errors')
    await page.screenshot({ path: resolve(output, 'AUTH-owner-invitation-mobile.png'), fullPage: true })
    await context.close()
    console.log('PASS secure Owner invitation acceptance, URL cleanup and mobile layout')
  }
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
    assert.equal(await t.page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--ink').trim()),
      '#000000', 'Light-screen text uses near-black instead of pale blue')
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
    // A delayed business list must not restore the picker after explicit sign-out.
    const t = await scenario('stale-businesses-after-logout', { multiple: true })
    let unblock
    const blocked = new Promise(resolve => { unblock = resolve })
    let received
    const requestReceived = new Promise(resolve => { received = resolve })
    let delivered
    const responseDelivered = new Promise(resolve => { delivered = resolve })
    await t.page.route('**/api/businesses', async route => {
      received()
      await blocked
      await response(route, 200, [business('Owner')])
      delivered()
    })
    await t.login()
    await requestReceived
    await t.page.getByRole('button', { name: 'Sign out' }).click()
    await t.page.getByRole('heading', { name: 'Sign in' }).waitFor()
    unblock()
    await responseDelivered
    await t.page.waitForTimeout(100)
    assert.equal(await t.page.getByRole('heading', { name: 'Sign in' }).count(), 1,
      'An old business-list response cannot replace the sign-in page')
    await t.check()
    await t.context.close()
    console.log('PASS AUTH stale business list ignored after sign-out')
  }

  for (const staleStatus of [200, 401]) {
    const t = await scenario('stale-dashboard-' + staleStatus, { multiple: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    let unblock
    const blocked = new Promise(resolve => { unblock = resolve })
    let received
    const requestReceived = new Promise(resolve => { received = resolve })
    let delivered
    const responseDelivered = new Promise(resolve => { delivered = resolve })
    await t.page.route('**/api/businesses/1/dashboard', async route => {
      received()
      await blocked
      await response(route, staleStatus,
        staleStatus === 200 ? dashboard('Owner') : { message: 'Old session expired' })
      delivered()
    })
    await t.page.getByRole('button', { name: 'Open Sample Retail as Owner' }).click()
    await requestReceived
    await t.page.getByRole('button', { name: 'Sign out' }).click()
    await t.page.getByRole('heading', { name: 'Sign in' }).waitFor()
    if (staleStatus === 401) {
      await t.login()
      await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    }
    unblock()
    await responseDelivered
    await t.page.waitForTimeout(100)
    if (staleStatus === 200) {
      assert.equal(await t.page.getByRole('heading', { name: 'Sign in' }).count(), 1,
        'An old dashboard response cannot restore the logged-out workspace')
    } else {
      assert.equal(await t.page.getByRole('heading', { name: 'Choose a business' }).count(), 1,
        'An old 401 must not log out the newly authenticated session')
    }
    await t.check()
    await t.context.close()
    console.log('PASS AUTH stale dashboard ' + staleStatus + ' ignored after auth transition')
  }

  {
    const t = await scenario('business-owner', { role: 'Owner' })
    await t.login(true)
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    await t.page.getByRole('heading', { name: 'Complete store setup' }).waitFor()
    const ownerText = await t.page.evaluate(() => ({
      heading: getComputedStyle(document.querySelector('.business-shell .business-setup-panel h2')).color,
      nav: getComputedStyle(document.querySelector('.business-shell .app-sidebar .nav-entry')).color
    }))
    assert.equal(ownerText.heading, 'rgb(0, 0, 0)', 'Light workspace headings are near-black')
    assert.equal(ownerText.nav, 'rgb(255, 255, 255)', 'Dark navigation text is pure white')
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
    assert.equal(await t.page.locator('.business-kpi-card').count(), 0,
      'Approvals should appear only as an action, not as a duplicate KPI')
    await t.page.getByRole('button', { name: /Review 3 draft purchase orders/ }).waitFor()
    await t.page.getByRole('heading', { name: 'Attention Needed' }).waitFor()
    assert.equal(await t.page.getByRole('heading', { name: 'Recent Activity' }).count(), 0,
      'An unsupported activity feed must not be an empty panel')
    assert.equal(await t.page.getByRole('heading', { name: 'Complete store setup' }).count(), 0)
    assert.equal(await t.page.locator('.business-action-card').count(), 0)
    await t.screenshot('BUS-01-owner-operational-desktop')
    await t.page.getByRole('button', { name: /Review 3 draft purchase orders/ }).click()
    await t.page.getByRole('heading', { name: 'Purchase Orders', level: 2 }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 operational owner three sections and review action')
  }

  {
    const t = await scenario('purchase-catalog-on-demand', {
      role: 'Owner', hasProducts: true, pendingApprovals: 2, productsFixture: true
    })
    const catalogReads = []
    t.page.on('request', request => {
      const url = new URL(request.url())
      if (/^\/api\/businesses\/1\/(suppliers|products)$/.test(url.pathname)) {
        catalogReads.push(url.pathname)
      }
    })
    await t.login()
    await t.page.getByRole('button', { name: /Review 2 draft purchase orders/ }).click()
    await t.page.getByRole('heading', { name: 'Purchase Orders', level: 2 }).waitFor()
    await t.page.getByText('No purchase orders yet.').waitFor()
    assert.equal(catalogReads.length, 0,
      'Reviewing purchase orders should not fetch supplier or product creation catalogs')

    await t.page.getByRole('button', { name: /New Purchase Order/ }).click()
    await t.page.getByRole('heading', { name: 'New purchase order', level: 3 }).waitFor()
    assert.equal(await t.page.getByRole('heading', { name: 'Purchase order list' }).count(), 0,
      'Order entry must not show the unrelated register')
    await t.page.locator('#purchase-supplier').click()
    await t.page.getByRole('option', { name: /Wholesaler/ }).waitFor()
    await t.page.locator('#purchase-product').click()
    await t.page.getByRole('option', { name: /Blue Shirt/ }).waitFor()
    assert.equal(await t.page.getByRole('combobox', { name: 'Supplier *' }).count(), 1)
    assert.equal(await t.page.locator('select#purchase-supplier, select#purchase-product').count(), 0)
    assert.equal(await t.page.getByLabel('Find supplier').count(), 0)
    assert.equal(await t.page.getByLabel('Find product').count(), 0)
    assert.ok(catalogReads.some(path => path.endsWith('/suppliers')),
      'Supplier lookup should load when the draft form opens')
    assert.ok(catalogReads.some(path => path.endsWith('/products')),
      'Product lookup should load when the draft form opens')
    await t.page.getByRole('button', { name: '+ New supplier' }).click()
    await t.page.locator('#new-supplier-name').fill('Not Saved')
    await t.page.locator('#new-supplier-phone').fill('1234567890')
    await t.page.getByRole('button', { name: 'Cancel adding supplier' }).click()
    await t.page.getByRole('button', { name: '+ New supplier' }).click()
    assert.equal(await t.page.locator('#new-supplier-name').inputValue(), '',
      'Cancel adding supplier must discard the inline supplier draft')
    assert.equal(await t.page.locator('#new-supplier-phone').inputValue(), '')
    await t.page.getByRole('button', { name: 'Cancel adding supplier' }).click()
    await t.page.locator('#purchase-supplier').fill('Wholesaler')
    await t.page.locator('#purchase-supplier-option-21').waitFor()
    await t.page.locator('#purchase-supplier').press('ArrowDown')
    await t.page.locator('#purchase-supplier').press('Enter')
    assert.match(await t.page.locator('#purchase-supplier').inputValue(), /Wholesaler/)
    await t.page.locator('#purchase-product').fill('Blue')
    await t.page.locator('#purchase-product-option-11').click()
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).fill('4')
    await t.page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await t.page.getByRole('heading', { name: 'New purchase order', level: 3 }).waitFor({ state: 'hidden' })
    await t.page.getByRole('button', { name: 'New Purchase Order' }).click()
    await t.page.getByRole('heading', { name: 'New purchase order', level: 3 }).waitFor()
    assert.equal(await t.page.locator('#purchase-supplier').inputValue(), '',
      'Canceled order must not keep the previous supplier')
    assert.equal(await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).count(), 0,
      'Canceled order must not keep previously selected product lines')
    await t.page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await t.check()
    await t.context.close()
    console.log('PASS Purchase catalogs load on demand; canceled drafts reopen empty')
  }

  for (const { retryPosting, converted } of [
    { retryPosting: false, converted: false },
    { retryPosting: true, converted: false },
    { retryPosting: false, converted: true }
  ]) {
    // The same UI path must work normally and after a draft was saved but posting failed.
    const t = await scenario(converted ? 'purchase-alternate-unit' : retryPosting ? 'purchase-post-recovery' : 'purchase-to-stock-browser-flow', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    if (converted) await t.page.route('**/api/businesses/1/products?*', route =>
      response(route, 200, { items: [{ id: 11, name: 'Blue Shirt', sku: 'BLUE-11',
        unitCode: 'KG', isActive: true, conversions: [{ unitId: 32, unitCode: 'BOX', factor: 12.5, isActive: true }] }],
        total: 1, page: 1, pageSize: 20 }))
    const factor = converted ? 12.5 : 1
    let order = null
    let receiving = null
    let stock = 0
    let receivingPosts = 0
    let receivingCreates = 0
    let postingAttempts = 0
    await t.page.route('**/api/businesses/1/purchase-orders**', route => {
      const request = route.request()
      const path = new URL(request.url()).pathname
      const method = request.method()
      if (path.endsWith('/purchase-orders') && method === 'GET') {
        const params = new URL(request.url()).searchParams
        const visible = order && (!params.has('draftOnly') || order.status === 'Draft')
        return response(route, 200, {
          items: visible ? [{ id: order.id, locationId: 1, locationName: 'Main Shop',
            supplierId: 21, supplierName: 'Wholesaler', status: order.status,
            createdAtUtc: '2026-11-23T02:00:00Z' }] : [],
          total: visible ? 1 : 0, page: 1, pageSize: 20
        })
      }
      if (path.endsWith('/purchase-orders') && method === 'POST') {
        const body = request.postDataJSON()
        assert.equal(body.locationId, 1)
        assert.equal(body.supplierId, 21)
        assert.deepEqual(body.lines.map(({ productId, quantity, unitCost }) =>
          ({ productId, quantity, unitCost })), [{ productId: 11, quantity: 2, unitCost: 40 }])
        if (converted) {
          assert.equal(body.lines[0].unitId, 32)
          assert.equal(body.lines[0].expectedFactor, factor)
          assert.equal(body.lines[0].expectedBaseUnitCode, 'KG')
        }
        order = { id: 71, status: 'Draft', quantity: 2 }
        return response(route, 201, { id: 71, status: 'Draft' })
      }
      if (path.endsWith('/purchase-orders/71') && method === 'GET') {
        const posted = receiving?.status === 'Posted' ? receiving.quantity : 0
        return response(route, 200, { id: 71, locationId: 1, supplierId: 21,
          status: order.status, createdAtUtc: '2026-11-23T02:00:00Z', approvedAtUtc: null,
          lines: [{ id: 72, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
            ...(converted ? { unitCode: 'BOX', baseUnitCode: 'KG', conversionFactor: factor,
              baseOrderedQuantity: 2 * factor, baseReceivedQuantity: posted * factor } : {}),
            orderedQuantity: 2, unitCost: 40, receivedQuantity: posted,
            remainingQuantity: 2 - posted }],
          receivings: receiving ? [{
            id: 91, status: receiving.status, createdAtUtc: '2026-11-23T02:01:00Z',
            postedAtUtc: receiving.status === 'Posted' ? '2026-11-23T02:02:00Z' : null,
            lines: [{ purchaseOrderLineId: 72, quantity: receiving.quantity }]
          }] : [] })
      }
      if (path.endsWith('/purchase-orders/71/approve') && method === 'POST') {
        assert.equal(order.status, 'Draft')
        order.status = 'Approved'
        return response(route, 200, { id: 71, status: 'Approved' })
      }
      if (path.endsWith('/purchase-orders/71/receivings') && method === 'POST') {
        assert.equal(order.status, 'Approved')
        const body = request.postDataJSON()
        assert.deepEqual(body.lines, [{ purchaseOrderLineId: 72, quantity: 2 }])
        receivingCreates++
        receiving = { quantity: 2, status: 'Draft' }
        return response(route, 201, { id: 91, status: 'Draft' })
      }
      if (path.endsWith('/purchase-orders/71/receivings/91/post') && method === 'POST') {
        postingAttempts++
        if (retryPosting && postingAttempts === 1)
          return response(route, 503, { message: 'Temporary posting outage' })
        assert.equal(receiving.status, 'Draft')
        receiving.status = 'Posted'
        stock += receiving.quantity * factor
        order.status = 'Closed'
        receivingPosts++
        return response(route, 200, { id: 91, status: 'Posted', orderClosed: true })
      }
      return response(route, 404, { message: 'Unexpected purchase route' })
    })
    await t.page.route(/\/api\/businesses\/1\/stock\/locations\/1(?:\?|$)/, route =>
      response(route, 200, {
        items: [{ id: 11, name: 'Blue Shirt', sku: 'BLUE-11',
          isActive: true, reorderLevel: 4, quantity: stock, hasStockRecord: stock > 0 }],
        total: 1, page: 1, pageSize: 20
      }))

    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Purchase Orders' }).click()
    await t.page.getByRole('button', { name: 'New Purchase Order' }).click()
    await t.page.locator('#purchase-supplier').click()
    await t.page.locator('#purchase-supplier-option-21').click()
    await t.page.locator('#purchase-product').click()
    await t.page.locator('#purchase-product-option-11').click()
    if (converted) {
      await t.page.getByLabel('Purchase unit for Blue Shirt').selectOption('32')
      await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).fill('0.001')
      await t.page.getByRole('spinbutton', { name: 'Blue Shirt unit cost' }).fill('40')
      assert.equal(await t.page.getByRole('button', { name: 'Save & Approve' }).isDisabled(), true)
    }
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).fill('2')
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt unit cost' }).fill('40')
    assert.equal(await t.page.getByRole('button', { name: 'Save Draft', exact: true }).count(), 1,
      'Owner can still save unfinished purchase orders as Draft')
    if (converted) await t.page.getByText('25 KG stock', { exact: true }).waitFor()
    await t.page.getByRole('button', { name: 'Save & Approve' }).click()
    await t.page.getByRole('heading', { name: 'Purchase order #71' }).waitFor()
    assert.equal(await t.page.getByRole('heading', { name: 'Purchase order list' }).count(), 0)
    await t.page.getByRole('button', { name: 'Receive goods' }).waitFor()
    assert.equal(order.status, 'Approved', 'Owner save must approve without a second click')
    assert.equal(stock, 0, 'Approval must not update stock')
    assert.equal(await t.page.getByRole('button', { name: 'Approve purchase order' }).count(), 0)
    await t.page.getByRole('button', { name: 'Receive goods' }).click()
    if (converted) {
      await t.page.getByRole('spinbutton', { name: 'Receive Blue Shirt' }).fill('0.001')
      assert.equal(await t.page.getByRole('button', { name: 'Receive & Post Stock' }).isDisabled(), true)
    }
    await t.page.getByRole('button', { name: 'Receive all remaining' }).click()
    if (converted) await t.page.getByText('25 KG stock', { exact: true }).waitFor()
    assert.equal(await t.page.getByRole('button', { name: 'Save Draft', exact: true }).count(), 1,
      'Unfinished receivings can still be saved as Draft')
    await t.page.getByRole('button', { name: 'Receive & Post Stock' }).click()
    if (retryPosting) {
      await t.page.getByText('Receiving #91 was saved, but stock posting could not be confirmed.',
        { exact: false }).waitFor()
      await t.page.getByRole('button', { name: 'Post stock' }).waitFor()
      const draftRow = t.page.locator('tbody tr').filter({ hasText: '#91' })
      assert.match(await draftRow.innerText(), /Blue Shirt.*2[.]000/,
        'A saved draft must show its exact product and quantity before posting')
      assert.match(await draftRow.innerText(), /Draft/)
      assert.equal(stock, 0, 'A posting failure must not change stock')
      await t.page.getByRole('button', { name: 'Post stock' }).click()
    }
    await t.page.getByText('Receiving #91 posted.', { exact: false }).waitFor()
    await t.page.getByText(converted ? 'Blue Shirt · 2.000 BOX' : 'Blue Shirt · 2.000', { exact: true }).waitFor()
    assert.equal(await t.page.locator('tbody tr').filter({ hasText: '#91' })
      .getByRole('button', { name: 'Post stock' }).count(), 0,
      'Posted receipts must not offer another stock posting')
    assert.equal(receivingCreates, 1, 'Retry after a post failure must reuse the same saved draft')
    assert.equal(await t.page.getByRole('button', { name: 'Review and post' }).count(), 0,
      'Do not reopen a redundant confirmation step after posting')
    assert.equal(receivingPosts, 1, 'Stock posting must only submit once')
    assert.equal(order.status, 'Closed')
    assert.equal(stock, 2 * factor)
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    const stockRow = t.page.locator('.retail-stock-list tbody tr').filter({ hasText: 'Blue Shirt' })
    await stockRow.getByText((2 * factor).toFixed(3)).waitFor()
    assert.equal(await stockRow.locator('td').nth(2).innerText(), (2 * factor).toFixed(3))
    await t.check()
    await t.context.close()
    console.log(converted ? 'PASS converted purchase and receiving validates exact base quantity' : retryPosting
      ? 'PASS saved receiving draft recovers from failed post without duplicate stock'
      : 'PASS one-click Owner save+approve, receive+post and inventory refresh')
  }

  {
    const t = await scenario('owner-optional-purchase-draft', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    let orderCreated = 0
    let approvals = 0
    let status = 'Draft'
    await t.page.route('**/api/businesses/1/purchase-orders**', route => {
      const path = new URL(route.request().url()).pathname
      const method = route.request().method()
      if (path.endsWith('/purchase-orders') && method === 'GET')
        return response(route, 200, { items: [], total: 0, page: 1, pageSize: 20 })
      if (path.endsWith('/purchase-orders') && method === 'POST') {
        orderCreated++
        return response(route, 201, { id: 82, status: 'Draft' })
      }
      if (path.endsWith('/purchase-orders/82') && method === 'GET')
        return response(route, 200, {
          id: 82, locationId: 1, supplierId: 21, status,
          lines: [{ id: 83, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
            orderedQuantity: 1, unitCost: 0, receivedQuantity: 0, remainingQuantity: 1 }],
          receivings: []
        })
      if (path.endsWith('/purchase-orders/82/approve') && method === 'POST') {
        approvals++
        status = 'Approved'
        return response(route, 200, { id: 82, status })
      }
      return response(route, 404, { message: 'Unexpected purchase route' })
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Purchase Orders' }).click()
    await t.page.getByRole('button', { name: 'New Purchase Order' }).click()
    await t.page.locator('#purchase-supplier').click()
    await t.page.locator('#purchase-supplier-option-21').click()
    await t.page.locator('#purchase-product').click()
    await t.page.locator('#purchase-product-option-11').click()
    await t.page.getByRole('button', { name: 'Save Draft', exact: true }).click()
    await t.page.getByRole('heading', { name: 'Purchase order #82' }).waitFor()
    await t.page.getByRole('button', { name: 'Approve purchase order' }).waitFor()
    assert.equal(orderCreated, 1)
    assert.equal(approvals, 0, 'Saving an unfinished Owner draft must not auto-approve')
    await t.page.getByRole('button', { name: 'Approve purchase order' }).click()
    await t.page.getByRole('button', { name: 'Receive goods' }).waitFor()
    assert.equal(approvals, 1)
    await t.check()
    await t.context.close()
    console.log('PASS Owner Save Draft stays pending until explicit approval')
  }

  {
    const t = await scenario('manager-purchase-owner-approval-boundary', {
      role: 'Manager', hasProducts: true, productsFixture: true
    })
    let createCount = 0
    let approvalRequests = 0
    await t.page.route('**/api/businesses/1/purchase-orders**', route => {
      const path = new URL(route.request().url()).pathname
      const method = route.request().method()
      if (path.endsWith('/purchase-orders') && method === 'GET')
        return response(route, 200, { items: [], total: 0, page: 1, pageSize: 20 })
      if (path.endsWith('/purchase-orders') && method === 'POST') {
        createCount++
        return response(route, 201, { id: 81, status: 'Draft' })
      }
      if (path.endsWith('/purchase-orders/81') && method === 'GET')
        return response(route, 200, {
          id: 81, locationId: 1, supplierId: 21, status: 'Draft',
          lines: [{ id: 82, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
            orderedQuantity: 2, unitCost: 40, receivedQuantity: 0, remainingQuantity: 2 }],
          receivings: []
        })
      if (path.endsWith('/approve')) approvalRequests++
      return response(route, 403, { message: 'Owner approval required' })
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Purchase Orders' }).click()
    await t.page.getByRole('button', { name: 'New Purchase Order' }).click()
    await t.page.locator('#purchase-supplier').click()
    await t.page.locator('#purchase-supplier-option-21').click()
    await t.page.locator('#purchase-product').click()
    await t.page.locator('#purchase-product-option-11').click()
    await t.page.getByRole('button', { name: 'Submit for Approval' }).click()
    await t.page.getByRole('heading', { name: 'Purchase order #81' }).waitFor()
    await t.page.getByText('Awaiting owner approval').waitFor()
    assert.equal(createCount, 1, 'Manager creates one draft order')
    assert.equal(approvalRequests, 0, 'Manager must never invoke Owner approval')
    assert.equal(await t.page.getByRole('button', { name: 'Approve purchase order' }).count(), 0)
    await t.check()
    await t.context.close()
    console.log('PASS Manager submits one PO draft without Owner approval privileges')
  }

  {
    const t = await scenario('owner-cash-summary-integrated', {
      role: 'Owner', hasProducts: true, cashSalesFixture: true
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').count(), 2,
      'Only verified cash metrics should occupy KPI cards')
    const heights = await t.page.locator('.business-kpi-card').evaluateAll(cards =>
      cards.map(card => card.getBoundingClientRect().height))
    assert.ok(Math.max(...heights) - Math.min(...heights) <= 1,
      'Verified KPI cards stay aligned in the desktop row')
    assert.ok((await t.page.locator('.business-kpi-card').first().innerText()).includes('150.25'),
      'Cash total must come from the API response')
    assert.ok((await t.page.locator('.business-kpi-card').nth(1).innerText()).includes('2'),
      'Bill count must come from the API response')
    await t.page.getByText('Cash receipt #41').waitFor()
    assert.equal(await t.page.locator('.business-receipt-activity li').count(), 1)
    await t.screenshot('BUS-01-cash-summary-integrated-desktop')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 owner summary consumes the cash API contract')
  }

  {
    const t = await scenario('owner-live-refresh', {
      role: 'Owner', hasProducts: true, cashSalesFixture: true, ownerPos: true
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    let total = 370.50
    let status = 200
    let calls = 0
    await t.page.route('**/api/businesses/1/dashboard', route => {
      calls++
      if (status !== 200) return response(route, status, { message: 'Temporary outage' })
      const result = dashboard('Owner', true, 0, 1, 1, true, false, true)
      result.cashSalesToday.total = total
      return response(route, 200, result)
    })
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).first().click()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Dashboard' }).first().click()
    await t.page.waitForFunction(() => document.querySelector('.business-kpi-card')?.textContent?.includes('370.5'))
    assert.ok(calls > 0, 'Dashboard return reloads the latest API totals')

    total = 410.75
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Open POS Checkout' }).click()
    await t.page.locator('.pos-shell').waitFor()
    await t.page.getByRole('button', { name: /Exit POS/ }).click()
    await t.page.waitForFunction(() => document.querySelector('.business-kpi-card')?.textContent?.includes('410.75'))
    assert.ok(calls > 1, 'POS exit reloads the owner dashboard')

    status = 503
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Dashboard' }).first().click()
    await t.page.getByText('Dashboard refresh failed.', { exact: false }).waitFor()
    assert.equal(await t.page.getByRole('heading', { name: 'Business Overview' }).count(), 1)
    assert.ok((await t.page.locator('.business-kpi-card').first().innerText()).includes('410.75'))

    status = 404
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Dashboard' }).first().click()
    await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').count(), 0)
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 fresh API totals, POS return, outage fallback and access loss')
  }

  {
    const t = await scenario('owner-invoice-print-preferences', {
      role: 'Owner', hasProducts: true, ownerPos: true
    })
    let putCount = 0
    let current = {
      paperSize: 'Thermal80', autoOpen: false, showTime: true, showUnitPrice: true,
      showCashReceived: true, showChange: true, showFooter: true,
      footerText: 'Thank you for shopping!', revision: 0,
      mandatoryFields: ['Business name', 'Receipt number', 'Date',
        'Items and quantities', 'Line totals', 'Total payable'],
      rendererAvailable: false
    }
    await t.page.route('**/api/businesses/1/receipt-settings', route => {
      if (route.request().method() === 'GET') return response(route, 200, current)
      assert.equal(route.request().method(), 'PUT')
      const requested = route.request().postDataJSON()
      assert.equal(requested.expectedRevision, current.revision)
      assert.ok(requested.reason.length >= 10)
      putCount++
      current = { ...current, ...requested, revision: current.revision + 1 }
      return response(route, 200, { settings: current, unchanged: false })
    })
    await t.login()
    await t.page.locator('.business-preview-nav').filter({ hasText: 'Business Settings' }).click()
    await t.page.getByRole('tab', { name: 'Invoice & Print' }).click()
    await t.page.getByRole('form', { name: 'Invoice print settings' }).waitFor()
    await t.page.getByText(/RDLC render\/print adapter is not connected yet/).waitFor()
    assert.ok((await t.page.getByText(/Always shown:/).innerText()).includes('Total payable'))
    await t.page.locator('#receipt-paper-size').selectOption('A4')
    await t.page.locator('#receipt-auto-open').selectOption('auto')
    await t.page.locator('#receipt-showTime').uncheck()
    await t.page.locator('#receipt-showUnitPrice').uncheck()
    await t.page.locator('#receipt-footer-text').fill('Please visit again')
    await t.page.locator('#receipt-settings-reason').fill('Retail owner approved this compact printed layout')
    await t.page.getByRole('button', { name: 'Save print settings' }).click()
    await t.page.locator('.action-feedback--success').waitFor()
    assert.equal(putCount, 1)
    assert.equal(current.paperSize, 'A4')
    assert.equal(current.autoOpen, true)
    assert.equal(current.showUnitPrice, false)
    assert.equal(current.revision, 1)
    await t.page.getByRole('tab', { name: 'General' }).click()
    await t.page.getByRole('tab', { name: 'Invoice & Print' }).click()
    assert.equal(await t.page.locator('#receipt-paper-size').inputValue(), 'A4')
    assert.equal(await t.page.locator('#receipt-auto-open').inputValue(), 'auto')
    assert.equal(await t.page.locator('#receipt-showTime').isChecked(), false)
    assert.equal(await t.page.locator('#receipt-showUnitPrice').isChecked(), false)
    assert.equal(await t.page.locator('#receipt-footer-text').inputValue(), 'Please visit again')
    await t.check()
    await t.context.close()
    console.log('PASS owner receipt preferences save/reload, mandatory fields and renderer-disabled notice')
  }

  {
    const t = await scenario('owner-billing-methods', {
      role: 'Owner', hasProducts: true, ownerPos: true
    })
    let cashOn = true
    let revision = 0
    let saves = 0
    let allowFirstSave
    let firstSaveStarted
    const firstSaveStartedPromise = new Promise(resolve => { firstSaveStarted = resolve })
    const firstSaveHold = new Promise(resolve => { allowFirstSave = resolve })
    await t.page.route('**/api/businesses/1/dashboard', route => response(route, 200, {
      ...dashboard('Owner', true, 0, 1, 1, false, false, true),
      cashPaymentEnabled: cashOn
    }))
    await t.page.route('**/api/businesses/1/billing-settings', async route => {
      if (route.request().method() === 'GET') return response(route, 200, {
        cashSalesEnabled: cashOn, creditSalesEnabled: false, creditAvailable: false,
        revision, salesPaused: !cashOn
      })
      assert.equal(route.request().method(), 'PUT')
      const input = route.request().postDataJSON()
      assert.equal(input.expectedRevision, revision)
      assert.equal(input.creditSalesEnabled, false)
      assert.ok(input.reason.length >= 10)
      saves++
      if (saves === 1) {
        firstSaveStarted()
        await firstSaveHold
      }
      cashOn = input.cashSalesEnabled
      revision++
      return response(route, 200, {
        settings: { cashSalesEnabled: cashOn, creditSalesEnabled: false,
          creditAvailable: false, revision, salesPaused: !cashOn },
        unchanged: false
      })
    })
    await t.page.route('**/api/businesses/1/cash-sales/products*', route => response(route, 200, [{
      id: 11, name: 'Preview Receipt Item', sku: 'PRE-11', barcode: null,
      salePrice: 10, available: 5, unitCode: 'PCS'
    }]))
    await t.login()
    await t.page.locator('.business-preview-nav').filter({ hasText: 'Business Settings' }).click()
    await t.page.getByRole('tab', { name: 'Billing & Payments' }).click()
    await t.page.getByRole('form', { name: 'Billing & Payments settings' }).waitFor()
    const cashToggle = t.page.locator('#billing-cash-toggle')
    const creditToggle = t.page.locator('#billing-credit-toggle')
    assert.equal(await cashToggle.isChecked(), true)
    assert.equal(await creditToggle.isDisabled(), true, 'Unreleased credit cannot be enabled')
    await cashToggle.uncheck()
    await t.page.getByLabel('Reason for changing payment options')
      .fill('Owner paused the store cash register')
    await t.page.getByRole('button', { name: 'Save payment settings' }).click()
    await firstSaveStartedPromise
    await t.page.locator('.action-feedback--saving').waitFor()
    assert.equal(await t.page.getByRole('button', { name: 'Saving…' }).isDisabled(), true,
      'Shared action button blocks repeat submit while request is pending')
    assert.equal(saves, 1)
    allowFirstSave()
    await t.page.locator('.action-feedback--success').waitFor()
    await t.page.getByText('Billing preferences saved.', { exact: false }).waitFor()
    assert.equal(saves, 1)
    assert.equal(cashOn, false)
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Open POS Checkout' }).click()
    await t.page.getByText(/Cash checkout is disabled in Business Settings/).waitFor()
    assert.equal(await t.page.getByRole('button', { name: /Add Preview Receipt Item/ }).isDisabled(), true)
    assert.equal(await t.page.getByRole('button', { name: 'Receipt history' }).isDisabled(), false)
    await t.check()
    await t.context.close()
    console.log('PASS BUS-54 Owner persists Cash OFF and POS stays history-accessible; Credit locked')
  }

  {
    const t = await scenario('owner-save-failure-retains-input', {
      role: 'Owner', hasProducts: true, ownerPos: true, width: 360, height: 780
    })
    let attempts = 0
    await t.page.route('**/api/businesses/1/billing-settings', async route => {
      if (route.request().method() === 'GET') {
        // Keep the reload in flight long enough to prove the input was restored
        // from the fresh response, not an old form DOM node.
        if (attempts > 0) await new Promise(resolve => setTimeout(resolve, 350))
        return response(route, 200, {
          cashSalesEnabled: true, creditSalesEnabled: false, creditAvailable: false,
          revision: 0, salesPaused: false
        })
      }
      attempts++
      if (attempts === 1)
        return response(route, 409, { message: 'Payment settings changed. Reload and retry.' })
      return route.abort('connectionreset')
    })
    await t.login()
    await t.page.getByRole('button', { name: 'Open navigation' }).click()
    await t.page.locator('.business-preview-nav').filter({ hasText: 'Business Settings' }).click()
    await t.page.getByRole('tab', { name: 'Billing & Payments' }).click()
    await t.page.locator('#billing-cash-toggle').uncheck()
    await t.page.getByLabel('Reason for changing payment options')
      .fill('Close register during store reconciliation')
    await t.page.getByRole('button', { name: 'Save payment settings' }).click()
    await t.page.locator('.action-feedback--error').waitFor()
    assert.ok((await t.page.locator('.action-feedback--error').innerText())
      .includes('Payment settings changed. Reload and retry.'))
    assert.equal(await t.page.locator('#billing-cash-toggle').isChecked(), false)
    assert.equal(await t.page.getByLabel('Reason for changing payment options').inputValue(),
      'Close register during store reconciliation', 'Failed write must preserve form inputs')
    assert.equal(await t.page.getByRole('button', { name: 'Save payment settings' }).isDisabled(), true,
      'Stale settings must be reloaded before another write')
    assert.equal(attempts, 1, 'No automatic retry for failed write')
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth <= 361),
      'Global save popup must not add horizontal overflow at 360px')
    await t.page.getByRole('button', { name: 'Dismiss notification' }).click()
    assert.equal(await t.page.locator('.action-feedback').count(), 0)
    await t.page.getByRole('button', { name: 'Reload settings (reset selection)' }).click()
    await t.page.getByText('Loading saved payment preferences…').waitFor()
    await t.page.locator('#billing-cash-toggle').waitFor()
    await t.page.locator('#billing-cash-toggle').uncheck()
    await t.page.getByRole('button', { name: 'Save payment settings' }).click()
    await t.page.locator('.action-feedback--uncertain').waitFor()
    assert.ok((await t.page.locator('.action-feedback--uncertain').innerText())
      .includes('The action may have completed. Check the record before retrying.'))
    assert.equal(attempts, 2, 'Lost response must not trigger automatic repeated mutation')
    assert.equal(await t.page.getByRole('button', { name: 'Save payment settings' }).isDisabled(), true,
      'Unknown outcome must be reconciled before another write')
    assert.equal(await t.page.locator('#billing-cash-toggle').isChecked(), false)
    assert.equal(await t.page.getByLabel('Reason for changing payment options').inputValue(),
      'Close register during store reconciliation')
    await t.check()
    await t.context.close()
    console.log('PASS shared save failure/unknown popups, input preservation and mobile layout')
  }

  {
    const t = await scenario('owner-billing-load-failure-recovery', {
      role: 'Owner', hasProducts: true, ownerPos: true
    })
    let loads = 0
    let loadRecovered = false
    await t.page.route('**/api/businesses/1/billing-settings', route => {
      loads++
      // React StrictMode may issue an initial GET twice. Keep both failed
      // until the user deliberately retries; only then return healthy data.
      if (!loadRecovered) return response(route, 503, { message: 'Billing preferences are unavailable.' })
      return response(route, 200, {
        cashSalesEnabled: true, creditSalesEnabled: false, creditAvailable: false,
        revision: 0, salesPaused: false
      })
    })
    await t.login()
    await t.page.locator('.business-preview-nav').filter({ hasText: 'Business Settings' }).click()
    await t.page.getByRole('tab', { name: 'Billing & Payments' }).click()
    await t.page.getByText('Billing preferences are unavailable.').waitFor()
    assert.equal(await t.page.getByText('Loading saved payment preferences…').count(), 0,
      'A failed GET must not show an infinite loading status')
    loadRecovered = true
    await t.page.getByRole('button', { name: 'Reload settings' }).click()
    await t.page.locator('#billing-cash-toggle').waitFor()
    assert.equal(await t.page.locator('#billing-cash-toggle').isChecked(), true)
    assert.ok(loads >= 2, 'The failed read was retried after the user requested it')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-54 failed settings load stops spinner and has working retry')
  }

  {
    const t = await scenario('pos-safe-barcode', { role: 'Cashier', hasProducts: true })
    const first = { id: 401, name: 'Scan Item', sku: 'SCAN-401', barcode: '900001',
      salePrice: 25, available: 3, unitCode: 'PCS' }
    const other = { id: 402, name: 'Other Item', sku: '900001', barcode: null,
      salePrice: 50, available: 5, unitCode: 'PCS' }
    let releaseLookup
    const lookupGate = new Promise(resolve => { releaseLookup = resolve })
    let releaseAmbiguous
    const ambiguousGate = new Promise(resolve => { releaseAmbiguous = resolve })
    const exactRequests = []
    await t.page.route('**/api/businesses/1/cash-sales/products*', async route => {
      const params = new URL(route.request().url()).searchParams
      const term = params.get('search') ?? ''
      if (params.get('exact') === 'true') {
        exactRequests.push(term)
        if (term === 'SCAN-401') {
          await lookupGate
          return response(route, 200, [first])
        }
        if (term === '900001') await ambiguousGate
      }
      return response(route, 200, [first, other])
    })
    await t.login()
    await t.page.getByRole('button', { name: /Add Scan Item/ }).waitFor()
    const search = t.page.locator('#pos-product-search')
    await search.fill('SCAN-401')
    await search.press('Enter')
    try {
      await t.page.getByText('Checking…', { exact: true }).waitFor()
      assert.equal(await t.page.locator('#pos-location').isDisabled(), true)
      assert.equal(await t.page.getByRole('button', { name: 'Exit POS' }).isDisabled(), true)
      assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true)
    } finally { releaseLookup() }
    const qty = t.page.getByRole('spinbutton', { name: 'Quantity of Scan Item' })
    await qty.waitFor()
    assert.equal(await qty.inputValue(), '1')
    await search.fill('900001')
    await search.press('Enter')
    try {
      await t.page.getByText('Checking…', { exact: true }).waitFor()
      assert.equal(await qty.isDisabled(), true)
      assert.equal(await t.page.getByRole('button', { name: 'Remove Scan Item' }).isDisabled(), true)
      assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true)
    } finally { releaseAmbiguous() }
    await t.page.getByRole('alert').getByText('This code matches multiple products. Select the correct product card.').waitFor()
    assert.equal(await qty.inputValue(), '1')
    assert.equal(await t.page.getByRole('spinbutton', { name: 'Quantity of Other Item' }).count(), 0)
    assert.deepEqual(exactRequests, ['SCAN-401', '900001'])
    await t.check()
    await t.context.close()
    console.log('PASS POS barcode verifies current location and refuses ambiguous product codes')
  }

  {
    const t = await scenario('pos-keyboard-shortcuts', { role: 'Cashier', hasProducts: true })
    const item = { id: 451, name: 'Keyboard Item', sku: 'KEY-451', barcode: '451001',
      salePrice: 25, available: 5, unitCode: 'PCS' }
    let posts = 0
    await t.page.route('**/api/businesses/1/cash-sales/products*', route => response(route, 200, [item]))
    await t.page.route('**/api/businesses/1/cash-sales', route => {
      posts++
      return response(route, 500, { message: 'Unexpected sale posting' })
    })
    await t.login()
    const product = t.page.getByRole('button', { name: /Add Keyboard Item/ })
    await product.waitFor()
    const search = t.page.locator('#pos-product-search')
    const pay = t.page.getByRole('button', { name: /Pay \/ Checkout/ })
    await t.page.keyboard.press('F9')
    assert.equal(await pay.isDisabled(), true)
    assert.equal(await t.page.getByRole('dialog').count(), 0)
    await product.focus()
    await t.page.keyboard.press('F2')
    assert.equal(await search.evaluate(el => el === document.activeElement), true)
    await t.page.keyboard.press('F4')
    const customerSearch = t.page.locator('#pos-customer-search')
    await customerSearch.waitFor()
    assert.equal(await customerSearch.evaluate(el => el === document.activeElement), true)
    // Leave the customer search open to check that cancelling payment still restores checkout focus.
    await product.click()
    await t.page.keyboard.press('F9')
    const dialog = t.page.getByRole('dialog', { name: 'Complete Payment' })
    await dialog.waitFor()
    const cash = t.page.getByLabel('Cash received')
    await t.page.waitForFunction(() => document.activeElement?.id === 'pos-cash-received')
    await t.page.keyboard.press('F2')
    await t.page.keyboard.press('F4')
    await t.page.keyboard.press('F9')
    assert.equal(await cash.evaluate(el => el === document.activeElement), true)
    assert.equal(posts, 0)
    await t.page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'hidden' })
    assert.equal(await pay.evaluate(el => el === document.activeElement), true)
    assert.equal(await t.page.getByRole('spinbutton', { name: 'Quantity of Keyboard Item' }).inputValue(), '1')
    assert.equal(posts, 0)
    await t.check()
    await t.context.close()
    console.log('PASS POS keyboard shortcuts preserve checkout validation and modal focus')
  }

  for (const mode of ['checkout', 'blocked-print', 'save-only', 'recover', 'review', 'out-of-stock', 'recover-review']) {
    const recover = mode === 'recover'
    const recoveringReview = mode === 'recover-review'
    const t = await scenario('pos-fractional-' + mode, {
      role: 'Cashier', hasProducts: true
    })
    if (['checkout', 'blocked-print', 'save-only'].includes(mode)) await t.page.route('**/api/businesses/1/dashboard', route => response(route, 200, {
      ...dashboard('Cashier', true), receiptAutoOpen: true
    }))
    let posted = null
    let posts = 0
    let originalRequest
    const receipt = {
      printAvailable: true,
      id: 501, total: 10, tendered: 10, currencyCode: 'INR',
      postedAtUtc: '2026-11-23T06:00:00Z',
      lines: [{ id: 502, productName: 'Loose Rice', unitPrice: 80, quantity: 0.125 }]
    }
    await t.page.route('**/api/businesses/1/cash-sales/receipt?*', route => {
      assert.equal(new URL(route.request().url()).searchParams.get('requestKey'), originalRequest.requestKey)
      return posts === 1 ? response(route, 404, {}) : response(route, 200, receipt)
    })
    await t.page.route('**/api/businesses/1/cash-sales/products*', route => {
      const term = new URL(route.request().url()).searchParams.get('search') ?? ''
      if (mode === 'out-of-stock' && posts > 0) return response(route, 200, [])
      return response(route, 200, term && !'Loose Rice'.toLowerCase().includes(term.toLowerCase())
        ? [] : [{
          id: 91, name: 'Loose Rice', sku: 'RICE-KG', barcode: null,
          salePrice: (mode === 'review' || recoveringReview) && posts > 0 ? 96 : 80,
          available: (mode === 'review' || recoveringReview) && posts > 0 ? 0.25 : 0.625, unitCode: 'KG'
        }])
    })
    await t.page.route('**/api/businesses/1/cash-sales', route => {
      if (route.request().method() !== 'POST')
        return response(route, 404, { message: 'Unexpected cash sale route' })
      posted = route.request().postDataJSON()
      posts++
      if (mode === 'out-of-stock') return response(route, 400, {
        code: 'CASH_SALE_BASKET_CHANGED', productId: 91,
        message: 'Insufficient stock for Loose Rice at this location.'
      })
      if (mode === 'review') {
        if (posts === 1) {
          originalRequest = posted
          return response(route, 400, {
            code: 'CASH_SALE_BASKET_CHANGED',
            productId: 91,
            message: 'Product or price changed. Remove and select the affected item again.'
          })
        }
        return response(route, 200, {
          ...receipt, total: 24, tendered: 30,
          lines: [{ id: 502, productName: 'Loose Rice', unitPrice: 96, quantity: 0.25 }]
        })
      }
      if (recoveringReview) {
        if (posts === 1) {
          originalRequest = posted
          return route.abort('connectionreset')
        }
        if (posts === 2) {
          assert.deepEqual(posted, originalRequest)
          return response(route, 400, {
            code: 'CASH_SALE_BASKET_CHANGED', productId: 91,
            message: 'Product or price changed. Remove and select the affected item again.'
          })
        }
        return response(route, 200, {
          ...receipt, total: 24, tendered: 30,
          lines: [{ id: 502, productName: 'Loose Rice', unitPrice: 96, quantity: 0.25 }]
        })
      }
      if (recover) {
        if (posts === 1) {
          originalRequest = posted
          return route.abort('connectionreset')
        }
        assert.deepEqual(posted, originalRequest)
        return response(route, 403, { message: 'Retry could not be authorized.' })
      }
      return response(route, 200, receipt)
    })
    await t.login()
    await t.page.locator('.pos-checkout').waitFor()
    await t.page.getByRole('button', { name: /Add Loose Rice/ }).waitFor()
    await t.page.getByRole('button', { name: /Add Loose Rice/ }).click()
    const kgQuantity = t.page.getByRole('spinbutton', { name: 'Quantity of Loose Rice' })
    assert.equal(await t.page.getByRole('button', { name: 'Print receipt' }).count(), 0)
    assert.equal(await kgQuantity.inputValue(), '0.625',
      'Adding limited-stock KG uses the exact available fractional quantity')
    await t.page.getByRole('button', { name: 'Decrease quantity of Loose Rice' }).click()
    assert.equal(await kgQuantity.inputValue(), '0.525',
      'Minus on a fractional unit must reduce quantity, not be disabled')
    await t.page.getByRole('button', { name: 'Increase quantity of Loose Rice' }).click()
    assert.equal(await kgQuantity.inputValue(), '0.625',
      'Plus can restore a fractional amount without exceeding available stock')
    assert.equal(await t.page.getByRole('button', { name: 'Increase quantity of Loose Rice' })
      .isDisabled(), true, 'Plus must not exceed stock')

    await t.page.locator('#pos-product-search').fill('not-in-catalog')
    await t.page.getByText('No products to display').waitFor()
    await kgQuantity.fill('0.900')
    await t.page.getByRole('alert').getByText('Only 0.625 KG available at this location.').waitFor()
    assert.equal(await kgQuantity.inputValue(), '0.625',
      'Filtered searches must not bypass the basket stock limit')
    await kgQuantity.fill('0.125')
    assert.equal(await kgQuantity.inputValue(), '0.125')
    await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    await t.page.getByRole('dialog', { name: 'Complete Payment' }).waitFor()
    await t.page.getByLabel('Cash received').fill('10')
    if (mode === 'checkout') {
      await t.context.route('**/api/businesses/1/cash-sales/receipts/501/pdf?*', route =>
        route.fulfill({ status: 200, contentType: 'text/plain', body: 'Synthetic RDLC PDF endpoint' }))
      const popupPromise = t.page.waitForEvent('popup')
      await t.page.getByRole('button', { name: 'Save & Print' }).click()
      const popup = await popupPromise
      await popup.waitForURL('**/cash-sales/receipts/501/pdf?locationId=1')
      await popup.close()
    } else if (mode === 'blocked-print') {
      await t.page.evaluate(() => { window.open = () => null })
      await t.page.getByRole('button', { name: 'Save & Print' }).click()
      await t.page.getByText('Sale saved. Use Print receipt to open the PDF.').waitFor()
    } else {
      await t.page.getByRole('button', { name: 'Save Only' }).click()
    }
    if (mode === 'out-of-stock') {
      await t.page.getByRole('alert').getByText(/Insufficient stock for Loose Rice/).waitFor()
      await t.page.getByText('Review required. Remove this item and select it again.').waitFor()
      assert.equal(await kgQuantity.inputValue(), '0.125')
      assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true)
      await t.page.getByRole('button', { name: 'Remove Loose Rice' }).click()
      await t.page.getByText('No products to display').waitFor()
      assert.equal(await t.page.getByRole('button', { name: /Add Loose Rice/ }).count(), 0)
      assert.equal(posts, 1)
      await t.check()
      await t.context.close()
      console.log('PASS POS-17 out-of-stock line cannot be repurchased from the refreshed catalog')
      continue
    }
    if (mode === 'review') {
      await t.page.getByRole('alert').getByText(/Product or price changed/).waitFor()
      await t.page.getByRole('dialog').waitFor({ state: 'hidden' })
      assert.equal(await kgQuantity.inputValue(), '0.125')
      assert.equal(posts, 1)
      await t.page.getByText('Review required. Remove this item and select it again.').waitFor()
      assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true)
      await t.page.getByRole('button', { name: 'Remove Loose Rice' }).click()
      await t.page.getByRole('button', { name: /Add Loose Rice/ }).click()
      assert.equal(await kgQuantity.inputValue(), '0.25')
      await t.page.getByText('₹96.00 / KG', { exact: true }).waitFor()
      await t.page.getByText('₹24.00', { exact: true }).first().waitFor()
      await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
      assert.equal(await t.page.getByLabel('Cash received').inputValue(), '')
      await t.page.getByLabel('Cash received').fill('30')
      await t.page.getByRole('button', { name: 'Save Only' }).click()
      await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
      const receiptView = t.page.getByRole('region', { name: 'Cash receipt' })
      await receiptView.getByText(/Total INR 24\.00/).waitFor()
      assert.equal(posts, 2)
      assert.notEqual(posted.requestKey, originalRequest.requestKey)
      assert.equal(posted.lines[0].quantity, 0.25)
      assert.equal(posted.lines[0].expectedUnitPrice, 96)
      assert.equal(posted.tendered, 30)
      await t.check()
      await t.context.close()
      console.log('PASS POS-17 changed price returns to the preserved basket for review')
      continue
    }
    if (recoveringReview) {
      await t.page.getByRole('button', { name: 'Record original request' }).click()
      await t.page.getByRole('alert').getByText(/Product or price changed/).waitFor()
      await t.page.getByText('Review required. Remove this item and select it again.').waitFor()
      assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true)
      await t.page.getByRole('button', { name: 'Remove Loose Rice' }).click()
      await t.page.getByRole('button', { name: /Add Loose Rice/ }).click()
      assert.equal(await kgQuantity.inputValue(), '0.25')
      await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
      await t.page.getByLabel('Cash received').fill('30')
      await t.page.getByRole('button', { name: 'Save Only' }).click()
      await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
      assert.equal(posts, 3)
      assert.notEqual(posted.requestKey, originalRequest.requestKey)
      assert.equal(posted.lines[0].expectedUnitPrice, 96)
      await t.check()
      await t.context.close()
      console.log('PASS POS-07 rejected recovery returns to editable basket with a new key')
      continue
    }
    if (recover) await t.page.getByRole('button', { name: 'Record original request' }).click()
    await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
    assert.equal(posts, recover ? 2 : 1)
    if (mode === 'blocked-print' || mode === 'save-only') {
      assert.equal(t.context.pages().length, 1)
      assert.equal(await t.page.getByText('Sale saved. Use Print receipt to open the PDF.').count(),
        mode === 'blocked-print' ? 1 : 0)
    }
    assert.equal(posted.lines.length, 1)
    assert.equal(posted.lines[0].productId, 91)
    assert.equal(posted.lines[0].quantity, 0.125)
    assert.equal(posted.tendered, 10)
    const printReceipt = t.page.getByRole('link', { name: 'Print receipt' })
    await printReceipt.waitFor()
    assert.equal(await printReceipt.getAttribute('href'), '/api/businesses/1/cash-sales/receipts/501/pdf?locationId=1')
    assert.equal(await printReceipt.getAttribute('target'), '_blank')
    assert.equal(posts, recover ? 2 : 1)
    await t.page.getByRole('button', { name: 'New sale', exact: true }).click()
    await t.page.getByText('Your basket is empty', { exact: true }).waitFor()
    assert.equal(await printReceipt.count(), 0)
    if (mode === 'checkout') {
      await t.page.route('**/api/businesses/1/cash-sales/receipts/501?*', route => {
        assert.equal(new URL(route.request().url()).searchParams.get('locationId'), '1')
        return response(route, 200, receipt)
      })
      await t.page.getByRole('button', { name: /Add Loose Rice/ }).click()
      assert.equal(await t.page.getByLabel('Receipt number').count(), 0,
        'Receipt lookup stays inside the compact history view')
      await t.page.getByRole('button', { name: 'Receipt history' }).click()
      await t.page.getByLabel('Receipt number').fill('501')
      await t.page.getByRole('button', { name: 'Find receipt' }).click()
      await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
      assert.equal(await printReceipt.getAttribute('href'), '/api/businesses/1/cash-sales/receipts/501/pdf?locationId=1')
      await t.page.getByRole('button', { name: 'Back to checkout' }).click()
      assert.equal(await kgQuantity.inputValue(), '0.625')
      await t.page.getByRole('button', { name: 'Back to products' }).click()
      assert.equal(await t.page.getByLabel('Receipt number').count(), 0,
        'Closing history restores the product catalogue')
      await t.page.route('**/api/businesses/1/cash-sales/receipts?*', route => {
        const params = new URL(route.request().url()).searchParams
        assert.equal(params.get('locationId'), '1')
        const newest = params.get('page') === '1'
        return response(route, 200, { items: [newest ? { ...receipt, id: 503 } : receipt], hasMore: newest })
      })
      await t.page.getByRole('button', { name: 'Receipt history' }).click()
      const history = t.page.getByRole('region', { name: 'Cash receipt history' })
      await history.getByRole('button', { name: 'Receipt #503' }).waitFor()
      await history.getByRole('button', { name: 'Next' }).click()
      await history.getByRole('button', { name: 'Receipt #501' }).click()
      await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
      await t.page.getByRole('button', { name: 'Back to checkout' }).click()
      assert.equal(await kgQuantity.inputValue(), '0.625')
      assert.equal(posts, 1)
    }
    await t.check()
    await t.context.close()
    console.log(recover ? 'PASS POS-07 preserves the original pending request after a denied retry' :
      'PASS POS sells fractional KG in base units with currency rounding')
  }

  {
    const t = await scenario('pos-alternate-unit-selection', { role: 'Cashier', hasProducts: true })
    let salePosts = 0
    await t.page.route('**/api/businesses/1/cash-sales/products*', route => response(route, 200, [
      { id: 401, name: 'Packed Water', sku: 'WATER', barcode: '12345', salePrice: 10,
        available: 30, unitCode: 'PCS', conversions: [
          { unitId: 41, unitCode: 'BOX', factor: 12, salePrice: 120 },
          { unitId: 42, unitCode: 'CARTON', factor: 48, salePrice: 480 }
        ] }
    ]))
    await t.page.route('**/api/businesses/1/cash-sales', route => {
      if (route.request().method() !== 'POST') return response(route, 404, {})
      const request = route.request().postDataJSON()
      salePosts++
      assert.equal(request.tendered, 240)
      assert.equal(request.lines.length, 1)
      const line = request.lines[0]
      assert.equal(line.productId, 401)
      assert.equal(line.unitId, 41)
      assert.equal(line.expectedFactor, 12)
      assert.equal(line.expectedBaseUnitCode, 'PCS')
      assert.equal(line.expectedUnitPrice, 120)
      assert.equal(line.quantity, 2)
      return response(route, 200, { id: 701, total: 240, tendered: 240, currencyCode: 'INR',
        postedAtUtc: '2026-11-25T04:00:00Z',
        lines: [{ id: 702, productName: 'Packed Water', unitCode: 'BOX', quantity: 2, unitPrice: 120, lineTotal: 240 }] })
    })
    await t.login()
    await t.page.getByRole('button', { name: /Add Packed Water/ }).click()
    const unit = t.page.getByLabel('Sale unit for Packed Water')
    await unit.selectOption('42')
    assert.equal(await unit.inputValue(), '0')
    await unit.selectOption('41')
    await t.page.getByText('12 PCS stock', { exact: true }).waitFor()
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹120\.00/)
    await t.page.getByRole('button', { name: 'Increase quantity of Packed Water' }).click()
    await t.page.getByText('24 PCS stock', { exact: true }).waitFor()
    assert.equal(await t.page.getByRole('button', { name: 'Increase quantity of Packed Water' }).isDisabled(), true)
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹240\.00/)
    const viewport = t.page.viewportSize()
    for (const width of [768, 360]) {
      await t.page.setViewportSize({ width, height: 800 })
      assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= width + 1)
      await t.screenshot('POS-unit-selection-' + width)
    }
    await t.page.setViewportSize(viewport)
    await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    await t.page.getByLabel('Cash received').fill('240')
    await t.page.getByRole('button', { name: 'Save Only' }).click()
    await t.page.getByRole('heading', { name: 'Cash receipt #701' }).waitFor()
    assert.equal(salePosts, 1)
    await t.check()
    await t.context.close()
    console.log('PASS POS alternate unit selection and base stock limit')
  }

  {
    const t = await scenario('pos-zero-value-fractional-line', {
      role: 'Cashier', hasProducts: true
    })
    let salePosts = 0
    await t.page.route('**/api/businesses/1/cash-sales/products*', route => response(route, 200, [
      { id: 301, name: 'Regular Item', sku: 'REG-301', barcode: null,
        salePrice: 2, available: 3, unitCode: 'PCS' },
      { id: 302, name: 'Tiny Item', sku: 'TINY-302', barcode: null,
        salePrice: 0.50, available: 3, unitCode: 'KG' },
      { id: 303, name: 'High Price Item', sku: 'HIGH-303', barcode: null,
        salePrice: 9999999999.03, available: 1, unitCode: 'PCS' }
    ]))
    await t.page.route('**/api/businesses/1/cash-sales', route => {
      if (route.request().method() !== 'POST') return response(route, 404, {})
      salePosts++
      const sent = route.request().postDataJSON()
      assert.equal(sent.tendered, 2.15)
      assert.equal(sent.lines.length, 2)
      assert.equal(sent.lines.find(line => line.productId === 302).quantity, 0.29)
      return response(route, 200, {
        id: 601, total: 2.15, tendered: 2.15, currencyCode: 'INR',
        postedAtUtc: '2026-11-24T05:00:00Z',
        lines: [
          { id: 602, productName: 'Regular Item', unitPrice: 2, quantity: 1 },
          { id: 603, productName: 'Tiny Item', unitPrice: 0.50, quantity: 0.29, lineTotal: 0.15 }
        ]
      })
    })
    await t.login()
    await t.page.getByRole('button', { name: /Add High Price Item/ }).click()
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹9999999999\.03/)
    await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    await t.page.getByLabel('Cash received').fill('9999999999.04')
    const highDialog = t.page.getByRole('dialog')
    await highDialog.locator('.pos-dialog-change').getByText('INR 0.01', { exact: true }).waitFor()
    assert.equal(await highDialog.getByRole('button', { name: 'Save Only' }).isEnabled(), true)
    await highDialog.getByRole('button', { name: 'Back to Cart' }).click()
    await t.page.getByRole('button', { name: 'Remove High Price Item' }).click()
    await t.page.getByRole('button', { name: /Add Regular Item/ }).click()
    await t.page.getByRole('button', { name: /Add Tiny Item/ }).click()
    const tinyQuantity = t.page.getByRole('spinbutton', { name: 'Quantity of Tiny Item' })
    await tinyQuantity.fill('0.001')
    await t.page.getByText(/A product line rounds to ₹0\.00/).waitFor()
    assert.equal(await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).isDisabled(), true,
      'Non-zero basket must still reject a product whose line rounds to zero')
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹2\.00/)
    assert.equal(salePosts, 0)
    await tinyQuantity.fill('0.010')
    assert.equal(await t.page.getByText(/A product line rounds to ₹0\.00/).count(), 0)
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹2\.01/)
    await tinyQuantity.fill('0.290')
    assert.match(await t.page.locator('.pos-total-row').innerText(), /₹2\.15/)
    const tinyLine = t.page.locator('.pos-basket-line').filter({ hasText: 'Tiny Item' })
    assert.equal(await tinyLine.locator('.pos-line-amount').innerText(), '₹0.15')
    await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    const dialog = t.page.getByRole('dialog')
    await dialog.getByText('INR 2.15', { exact: true }).waitFor()
    await t.page.getByLabel('Cash received').fill('2.14')
    assert.equal(await dialog.getByRole('button', { name: 'Save Only' }).isDisabled(), true)
    await t.page.getByLabel('Cash received').fill('2.151')
    assert.equal(await dialog.getByRole('button', { name: 'Save Only' }).isDisabled(), true)
    // 2^52 - 1 cents = 45035996273704.95, inclusive.
    await t.page.getByLabel('Cash received').fill('45035996273704.95')
    assert.equal(await dialog.getByRole('button', { name: 'Save Only' }).isEnabled(), true)
    await t.page.getByLabel('Cash received').fill('45035996273704.96')
    assert.equal(await dialog.getByRole('button', { name: 'Save Only' }).isDisabled(), true)
    await t.page.getByLabel('Cash received').fill('2.15')
    await t.page.getByRole('button', { name: 'Save Only' }).click()
    await t.page.getByRole('heading', { name: 'Cash receipt #601' }).waitFor()
    assert.equal(salePosts, 1)
    await t.check()
    await t.context.close()
    console.log('PASS POS-06 zero-value fractional line blocked; valid cent checkout succeeds')
  }

  {
    const t = await scenario('stock-history-source-references', {
      role: 'Owner', hasProducts: true, stockAlertsFixture: true
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    await t.page.getByText('Blue Shirt', { exact: true }).waitFor()
    await t.page.getByRole('button', { name: 'History' }).first().click()
    await t.page.getByRole('heading', { name: 'Stock movement history' }).waitFor()
    await t.page.getByText('Source line #89').waitFor()
    await t.page.getByText('Source line #77').waitFor()
    const sourceTable = t.page.locator('.retail-stock-list .product-form')
      .filter({ has: t.page.getByRole('heading', { name: 'Stock movement history' }) })
      .locator('table')
    assert.match(await sourceTable.innerText(), /Goods received/)
    assert.match(await sourceTable.innerText(), /Cash sale/)
    await t.check()
    await t.context.close()
    console.log('PASS inventory movement history shows receiving and cash sale source line IDs')
  }

  {
    const t = await scenario('owner-verified-stock-alerts', {
      role: 'Owner', hasProducts: true, stockAlertsFixture: true, locationCount: 2,
      width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.getByText('2 stock alerts · 1 out of stock', { exact: true }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').filter({ hasText: 'Stock Alerts' }).count(), 0)
    assert.equal(await t.page.locator('.business-stock-alerts li').count(), 2)
    await t.page.locator('.business-stock-alerts li').filter({ hasText: 'Out of stock' }).waitFor()
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Configured stock alerts must not cause horizontal overflow')
    await t.screenshot('BUS-01-verified-stock-alerts-mobile-360')
    await t.page.getByRole('button', { name: 'View stock alerts in Inventory' }).click()
    await t.page.getByRole('heading', { name: 'Inventory', exact: true, level: 2 }).waitFor()
    assert.equal(await t.page.locator('#stock-location').inputValue(), '1',
      'Dashboard must select the location of the first real alert')
    const alertsToggle = t.page.getByRole('checkbox', { name: /Alerts only/ })
    assert.ok(await alertsToggle.isChecked(), 'Alerts Only should be active after dashboard navigation')
    await t.page.getByText('Blue Shirt', { exact: true }).waitFor()
    assert.equal(await t.page.getByText('Fine Notebook').count(), 0)
    assert.equal(await t.page.getByLabel('Stock adjustments').count(), 0,
      'Alert filtering should not open the unrelated adjustment workflow')
    assert.equal(await t.page.locator('.masters-pages').count(), 0,
      'Single-page alerts should not show disabled pagination controls')
    assert.ok(t.stockQueries.some(path => path.includes('/locations/1?') && path.includes('alertsOnly=true')),
      'Server must filter records at the selected location, not the entire business')
    await t.screenshot('BUS-27-stock-alerts-filter-mobile-360')
    await t.page.locator('#stock-location').selectOption('2')
    await t.page.getByText('Red Bag', { exact: true }).waitFor()
    assert.ok(await alertsToggle.isChecked(), 'Location switching retains Alerts Only')
    assert.ok(t.stockQueries.some(path => path.includes('/locations/2?') && path.includes('alertsOnly=true')))
    await t.screenshot('BUS-27-alerts-other-location-mobile-360')
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Alerts table and filter should not overflow mobile viewport')
    const historyButton = t.page.getByRole('button', { name: 'History' }).first()
    const adjustButton = t.page.getByRole('button', { name: 'Adjust' }).first()
    assert.ok(await historyButton.evaluate(button =>
      button.getBoundingClientRect().right <= window.innerWidth - 15),
      'History action must be visible without horizontal table scrolling')
    assert.ok(await adjustButton.evaluate(button =>
      button.getBoundingClientRect().right <= window.innerWidth - 15),
      'Adjust action must be visible without horizontal table scrolling')
    await t.page.locator('#stock-location').selectOption('1')
    await t.page.getByText('Blue Shirt', { exact: true }).waitFor()
    await alertsToggle.uncheck()
    await t.page.getByText('Fine Notebook', { exact: true }).waitFor()
    assert.ok(t.stockQueries.some(path => path.includes('/locations/1?') && !path.includes('alertsOnly=true')),
      'Unchecking restores the unfiltered Inventory API query')
    await t.check()
    await t.context.close()
    console.log('PASS owner configured stock alerts / navigation / mobile')
  }


  // Business owner: one compact master workspace presents each level independently.
  for (const width of [1366, 360]) {
    const t = await scenario('product-master-levels-' + width, {
      role: 'Owner', hasProducts: true, productsFixture: true, width, height: 780
    })
    const tree = [
      { id: 1, name: 'General', parentId: null, isActive: true },
      { id: 2, name: 'Clothing', parentId: null, isActive: true },
      { id: 21, name: 'Menswear', parentId: 2, isActive: true },
      { id: 31, name: 'Shirts', parentId: 21, isActive: true }
    ]
    await t.page.route('**/api/businesses/1/products/options*', route =>
      response(route, 200, {
        categories: tree, brands: [{ id: 41, name: 'Raymond', isActive: true }],
        units: [{ id: 51, name: 'Pieces', code: 'PCS', isActive: true }]
      }))
    await t.page.route('**/api/businesses/1/categories?*', route => {
      const params = new URL(route.request().url()).searchParams
      const level = Number(params.get('level'))
      const parent = Number(params.get('parentId') || 0)
      const items = tree.filter(item =>
        (level === 1 ? item.parentId === null :
          level === 2 ? item.parentId === 2 : level === 3 ? item.parentId === 21 : true) &&
        (!parent || item.parentId === parent))
      return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
    })
    await t.page.route('**/api/businesses/1/brands?*', route =>
      response(route, 200, {
        items: [{ id: 41, name: 'Raymond', isActive: true }], total: 1, page: 1, pageSize: 20
      }))
    await t.page.route('**/api/businesses/1/units?*', route =>
      response(route, 200, {
        items: [{ id: 51, name: 'Pieces', code: 'PCS', isActive: true }],
        total: 1, page: 1, pageSize: 20
      }))
    await t.login()
    if (width === 360) await t.page.getByRole('button', { name: 'Open navigation' }).click()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Product Masters' }).click()
    await t.page.getByRole('heading', { name: 'Product Masters' }).waitFor()
    assert.equal(await t.page.locator('.business-master-tabs button').count(), 5)
    await t.page.getByRole('button', { name: 'Subgroups', exact: true }).click()
    await t.page.getByLabel('Group *').selectOption('2')
    await t.page.locator('#master-filter-parent').selectOption('2')
    await t.page.getByText('Menswear', { exact: true }).first().waitFor()
    await t.page.getByRole('button', { name: 'Categories', exact: true }).click()
    await t.page.getByLabel('Subgroup *').selectOption('21')
    await t.page.locator('#master-filter-parent').selectOption('21')
    await t.page.getByText('Shirts', { exact: true }).first().waitFor()
    await t.page.getByRole('button', { name: 'Brands', exact: true }).click()
    await t.page.getByText('Raymond', { exact: true }).waitFor()
    await t.page.getByRole('button', { name: 'Units', exact: true }).click()
    await t.page.getByText('Pieces', { exact: true }).waitFor()
    if (width === 360) assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'All five master tabs must remain readable without horizontal page overflow')
    await t.screenshot('BUS-10-classification-masters-' + width)
    await t.check()
    await t.context.close()
    console.log('PASS independent Group/Subgroup/Category/Brand/Unit master tabs at', width)
  }

  {
    const t = await scenario('owner-pos-credit-recovery', { role: 'Owner', hasProducts: true, ownerPos: true })
    let savedCredit = null
    let posts = 0
    await t.page.route('**/api/businesses/1/dashboard', route => response(route, 200, {
      ...dashboard('Owner', true, 0, 1, 1, false, false, true), creditPaymentEnabled: true
    }))
    await t.page.route('**/api/businesses/1/cash-sales/products?*', route => response(route, 200, [
      { id: 71, name: 'Credit Item', sku: 'CREDIT-71', barcode: null, salePrice: 100, available: 10, unitCode: 'PCS' }
    ]))
    await t.page.route('**/api/businesses/1/cash-sales/customers?*', route => response(route, 200, [
      { id: 61, name: 'Credit Customer', phone: '1234567890' }
    ]))
    await t.page.route('**/api/businesses/1/credit-sales', async route => {
      const req = route.request().postDataJSON()
      posts++
      assert.equal(req.customerId, 61)
      assert.equal(req.tendered, 0)
      assert.equal(req.dueDate, '2026-12-02')
      assert.equal(req.lines[0].productId, 71)
      savedCredit = { id: 81, requestKey: req.requestKey, customerId: 61, customerName: 'Credit Customer',
        creditDueDate: req.dueDate, paymentMethod: 'Credit', total: 100, outstanding: 100, tendered: 0,
        currencyCode: 'INR', postedAtUtc: '2026-11-25T04:00:00Z', printAvailable: true,
        lines: [{ id: 82, productName: 'Credit Item', quantity: 1, unitPrice: 100, lineTotal: 100, unitCode: 'PCS' }] }
      return route.abort('failed')
    })
    await t.page.route('**/api/businesses/1/credit-sales/receipt?*', route => {
      const key = new URL(route.request().url()).searchParams.get('requestKey')
      return savedCredit?.requestKey === key ? response(route, 200, savedCredit) : response(route, 404, {})
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Open POS Checkout' }).click()
    await t.page.getByRole('button', { name: /Add Credit Item/ }).click()
    await t.page.getByLabel('Payment method', { exact: true }).selectOption('Credit')
    assert.equal(await t.page.getByRole('button', { name: 'Record credit sale' }).isDisabled(), true)
    await t.page.getByRole('button', { name: 'Select customer' }).click()
    await t.page.getByPlaceholder('Customer name or phone').fill('Credit')
    await t.page.getByRole('button', { name: 'Credit Customer · 1234567890' }).click()
    await t.page.getByLabel('Due date', { exact: true }).fill('2026-12-02')
    await t.page.getByLabel('Confirm unpaid credit sale').check()
    await t.page.getByRole('button', { name: 'Record credit sale' }).click()
    await t.page.getByRole('heading', { name: 'Credit invoice #81' }).waitFor()
    assert.equal(posts, 1)
    assert.equal(await t.page.getByRole('link', { name: 'Print receipt', exact: true }).count(), 0)
    assert.equal(await t.page.getByRole('link', { name: 'Print credit invoice' }).getAttribute('href'),
      '/api/businesses/1/credit-sales/business-invoices/81/pdf?locationId=1')
    await t.screenshot('credit-invoice-posted')
    await t.page.getByRole('button', { name: 'New sale' }).click()
    await t.page.getByRole('heading', { name: 'New Sale', exact: true }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS owner POS credit recovery without duplicate posting')
  }

  {
    const t = await scenario('owner-credit-payment-recovery', { role: 'Owner', hasProducts: true })
    let saved = null
    let posts = 0
    await t.page.route('**/api/businesses/1/customers?*', route => response(route, 200, {
      items: [{ id: 61, name: 'Credit Customer', phone: '1234567890', email: '', isActive: true, creditLimit: 500 }],
      total: 1, page: 1, pageSize: 20
    }))
    await t.page.route('**/api/businesses/1/customers/61/credit-ledger?*', route => response(route, 200, {
      customerId: 61, name: 'Credit Customer', creditLimit: 500, canCollect: true,
      totals: [{ currencyCode: 'INR', debit: 100, paid: saved ? 40 : 0, outstanding: saved ? 60 : 100, invoiceCount: 1 }],
      items: [{ id: 81, locationName: 'Main Shop', currencyCode: 'INR', debit: 100, paid: saved ? 40 : 0,
        outstanding: saved ? 60 : 100, creditDueDate: '2026-12-02', postedAtUtc: '2026-11-25T04:00:00Z' }],
      hasMore: false
    }))
    await t.page.route('**/api/businesses/1/customers/61/credit-invoices/81/payments*', async route => {
      if (route.request().method() === 'POST') {
        posts++
        const req = route.request().postDataJSON()
        assert.equal(req.amount, 40)
        assert.equal(req.expectedOutstanding, 100)
        assert.equal(req.cashReceivedConfirmed, true)
        saved = { id: 91, saleId: 81, amount: req.amount, requestKey: req.requestKey,
          method: 'Cash', postedAtUtc: '2026-11-25T04:00:00Z' }
        return route.abort('failed')
      }
      const key = new URL(route.request().url()).searchParams.get('requestKey')
      return response(route, 200, { items: saved && (!key || key === saved.requestKey) ? [saved] : [], hasMore: false })
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Customers' }).first().click()
    assert.equal(await t.page.locator('#contact-name').count(), 0, 'Customer register opens without an entry form')
    await t.page.getByRole('button', { name: 'Add Customer', exact: true }).click()
    await t.page.getByRole('heading', { name: 'New customer' }).waitFor()
    assert.equal(await t.page.locator('#contact-search').count(), 0)
    await t.page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await t.page.getByRole('button', { name: 'Credit ledger for Credit Customer' }).click()
    assert.equal(await t.page.locator('#contact-search').count(), 0, 'Ledger opens without the customer register')
    await t.page.getByRole('button', { name: 'Receive payment #81' }).click()
    await t.page.getByLabel('Cash received (INR)', { exact: true }).fill('40')
    assert.equal(await t.page.getByRole('button', { name: 'Record payment', exact: true }).isDisabled(), true)
    await t.page.getByLabel('Cash already received for this invoice.').check()
    await t.page.getByRole('button', { name: 'Record payment', exact: true }).click()
    await t.page.getByRole('button', { name: 'Check saved payment' }).waitFor()
    await t.page.getByRole('button', { name: 'Close payment' }).click()
    await t.page.getByRole('button', { name: 'Close ledger' }).click()
    await t.page.getByRole('button', { name: 'Credit ledger for Credit Customer' }).click()
    await t.page.getByRole('button', { name: 'Check saved payment' }).click()
    await t.page.getByText('Payment #91 recorded.', { exact: false }).waitFor()
    await t.page.getByRole('button', { name: 'Receive payment #81' }).waitFor()
    assert.equal(posts, 1)
    await t.page.getByRole('button', { name: 'Receive payment #81' }).click()
    await t.page.getByRole('cell', { name: '#91', exact: true }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS owner credit payment recovery without duplicate posting')
  }

  {
    const t = await scenario('product-inline-category-unit-brand', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    const categories = [
      { id: 1, name: 'General', parentId: null, isActive: true },
      { id: 2, name: 'Clothing', parentId: null, isActive: true },
      { id: 21, name: 'Menswear', parentId: 2, isActive: true }
    ]
    const units = [{ id: 1, name: 'Item', code: 'ITEM' }]
    const brands = []
    let savedProduct = null
    await t.page.route('**/api/businesses/1/products/options*', route =>
      response(route, 200, { categories, units, brands }))
    await t.page.route('**/api/businesses/1/categories', route => {
      if (route.request().method() !== 'POST') return route.fallback()
      const body = route.request().postDataJSON()
      assert.equal(body.name, 'Shirts')
      assert.equal(body.parentId, 21)
      categories.push({ id: 31, name: body.name, parentId: body.parentId, isActive: true })
      return response(route, 201, { id: 31, name: body.name, parentId: 2, isActive: true })
    })
    await t.page.route('**/api/businesses/1/units', route => {
      if (route.request().method() !== 'POST') return route.fallback()
      const body = route.request().postDataJSON()
      assert.equal(body.name, 'Box')
      assert.equal(body.code, 'BOX')
      units.push({ id: 32, name: body.name, code: body.code })
      return response(route, 201, { id: 32, name: body.name, code: body.code, isActive: true })
    })
    await t.page.route('**/api/businesses/1/brands', route => {
      if (route.request().method() !== 'POST') return route.fallback()
      const body = route.request().postDataJSON()
      assert.equal(body.name, 'Raymond')
      brands.push({ id: 33, name: body.name })
      return response(route, 201, { id: 33, name: body.name, isActive: true })
    })
    await t.page.route('**/api/businesses/1/products', route => {
      if (route.request().method() !== 'POST') return route.fallback()
      savedProduct = route.request().postDataJSON()
      return response(route, 201, { id: 41 })
    })

    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).click()
    await t.page.getByRole('button', { name: 'Add Product' }).click()
    await t.page.getByRole('heading', { name: 'New product' }).waitFor()
    await t.page.locator('#product-name').fill('Cotton Shirt')
    await t.page.locator('#product-price').fill('1299')
    await t.page.locator('details summary').filter({ hasText: 'More details' }).click()

    await t.page.locator('#product-group').selectOption('2')
    await t.page.locator('#product-subgroup').selectOption('21')
    await t.page.getByRole('button', { name: '+ Add category' }).click()
    await t.page.locator('#inline-master-name').fill('Shirts')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() =>
      document.querySelector('#product-category')?.value === '31')
    assert.equal(await t.page.locator('#product-category option:checked').innerText(), 'Shirts')
    assert.equal(await t.page.locator('#product-brand').inputValue(), '0')

    await t.page.getByRole('button', { name: '+ Add unit' }).click()
    await t.page.locator('#inline-master-name').fill('Box')
    await t.page.locator('#inline-master-code').fill('BOX')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() => document.querySelector('#product-unit')?.value === '32')

    await t.page.getByRole('button', { name: '+ Add brand' }).click()
    await t.page.locator('#inline-master-name').fill('Raymond')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() => document.querySelector('#product-brand')?.value === '33')
    await t.page.locator('details summary').filter({ hasText: 'Alternate units (optional)' }).click()
    await t.page.getByRole('button', { name: '+ Add alternate unit' }).click()
    await t.page.getByLabel('Alternate unit 1').selectOption('1')
    await t.page.getByLabel('Conversion factor 1').fill('0')
    assert.equal(await t.page.getByRole('button', { name: 'Save product' }).isDisabled(), true)
    await t.page.getByLabel('Conversion factor 1').fill('0.125')
    await t.page.getByLabel('Alternate selling price 1').fill('0')
    assert.equal(await t.page.getByRole('button', { name: 'Save product' }).isDisabled(), true)
    await t.page.getByLabel('Alternate selling price 1').fill('150.25')
    await t.page.getByRole('button', { name: 'Save product' }).click()
    await t.page.getByText('Product added.', { exact: false }).waitFor()
    assert.equal(savedProduct.name, 'Cotton Shirt')
    assert.equal(savedProduct.categoryId, 31)
    assert.equal(savedProduct.unitId, 32)
    assert.equal(savedProduct.brandId, 33)
    assert.equal(savedProduct.salePrice, 1299)
    assert.deepEqual(savedProduct.conversions, [{ unitId: 1, factor: 0.125, salePrice: 150.25 }])
    await t.check()
    await t.context.close()
    console.log('PASS Product inline category, unit and brand creation without leaving form')
  }

  {
    const t = await scenario('product-inline-create-refresh-failure', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    let reads = 0
    let brandCreates = 0
    let productCreates = 0
    await t.page.route('**/api/businesses/1/products/options*', route => {
      reads++
      if (brandCreates > 0) return response(route, 503, { message: 'Options service offline' })
      return response(route, 200, {
        categories: [{ id: 1, name: 'General', parentId: null, isActive: true }],
        units: [{ id: 1, name: 'Item', code: 'ITEM', isActive: true }], brands: []
      })
    })
    await t.page.route('**/api/businesses/1/brands', route => {
      brandCreates++
      assert.equal(route.request().method(), 'POST')
      assert.equal(route.request().postDataJSON().name, 'Fresh Brand')
      return response(route, 201, { id: 77, name: 'Fresh Brand', isActive: true })
    })
    await t.page.route('**/api/businesses/1/products', route => {
      productCreates++
      assert.equal(route.request().method(), 'POST')
      assert.equal(route.request().postDataJSON().brandId, 77)
      return response(route, 201, { id: 78 })
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).click()
    await t.page.getByRole('button', { name: 'Add Product' }).click()
    await t.page.locator('#product-name').fill('Checked Product')
    await t.page.locator('#product-price').fill('25')
    await t.page.locator('details summary').filter({ hasText: 'More details' }).click()
    await t.page.getByRole('button', { name: '+ Add brand' }).click()
    const input = t.page.locator('#inline-master-name')
    await input.fill(' ')
    await input.press('Enter')
    assert.equal(brandCreates, 0, 'Enter must respect validation and cannot bypass disabled Add')
    assert.equal(await t.page.getByRole('button', { name: 'Save product' }).isDisabled(), true,
      'Product submission must wait for inline master resolution')
    await input.fill('Fresh Brand')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.getByText(/Master created and selected, but the options list could not refresh/).waitFor()
    assert.equal(brandCreates, 1)
    assert.equal(await t.page.getByLabel('Add brand').count(), 0,
      'Successful create must close the inline panel even if read refresh failed')
    assert.equal(await t.page.locator('#product-brand').inputValue(), '77',
      'The committed Brand ID must remain selected as an optimistic fallback')
    await t.page.getByRole('button', { name: 'Save product' }).click()
    await t.page.getByText('Product added.', { exact: false }).waitFor()
    assert.equal(productCreates, 1)
    assert.equal(brandCreates, 1, 'Failed options GET must not cause duplicate master creates')
    await t.check()
    await t.context.close()
    console.log('PASS Product inline create cannot duplicate on options refresh failure')
  }

  {
    const t = await scenario('owner-product-reorder-control', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).first().click()
    await t.page.getByRole('heading', { name: 'Products', exact: true, level: 2 }).waitFor()
    await t.page.getByRole('button', { name: 'Edit' }).first().click()
    assert.equal(await t.page.locator('#product-reorder').inputValue(), '4')
    await t.page.locator('#product-reorder').fill('3.5')
    assert.equal(await t.page.locator('#product-reorder').inputValue(), '3.5')
    await t.screenshot('BUS-06-reorder-level-edit-desktop')
    await t.check()
    await t.context.close()
    console.log('PASS product reorder threshold edit UI')
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
    const t = await scenario('owner-cash-summary-mobile', {
      role: 'Owner', hasProducts: true, cashSalesFixture: true, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.getByText('Cash receipt #41').waitFor()
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Connected cash reporting must not cause horizontal mobile overflow')
    await t.screenshot('BUS-01-owner-cash-summary-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-01 connected cash reporting on 360px mobile')
  }

  {
    const t = await scenario('customer-outstanding-report', { role: 'Owner', hasProducts: true })
    await t.page.route('**/api/businesses/1/customers/outstanding-report?*', route => {
      const params = new URL(route.request().url()).searchParams
      const empty = params.get('search') === 'missing'
      const overdue = params.get('overdueOnly') === 'true'
      return response(route, 200, { businessDate: '2026-11-25', timeZoneId: 'Asia/Kolkata',
        totals: empty ? [] : [{ currencyCode: 'INR', outstanding: overdue ? 30 : 100,
          notDue: overdue ? 0 : 70, overdue: 30, days1To30: 20, days31To60: 10,
          days61To90: 0, daysOver90: 0, invoiceCount: overdue ? 2 : 3 }],
        items: empty ? [] : [{ customerId: 61, name: 'Credit Customer', isActive: false,
          currencyCode: 'INR', outstanding: overdue ? 30 : 100, overdue: 30,
          oldestDueDate: '2026-10-20', invoiceCount: overdue ? 2 : 3 }], page: 1, hasMore: false })
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Reports' }).first().click()
    await t.page.getByRole('tab', { name: 'Outstanding' }).click()
    await t.page.getByRole('heading', { name: 'Customer Outstanding' }).waitFor()
    await t.page.getByRole('row').filter({ hasText: 'Credit Customer' }).getByText('100.00', { exact: true }).waitFor()
    await t.page.getByRole('row').filter({ hasText: 'Credit Customer' }).getByText('Inactive', { exact: true }).waitFor()
    await t.page.getByLabel('Show', { exact: true }).selectOption('overdue')
    await t.page.getByRole('row').filter({ hasText: 'Credit Customer' }).getByText('100.00', { exact: true }).waitFor({ state: 'hidden' })
    await t.page.getByRole('table', { name: 'Customer balances' }).getByText('Credit Customer', { exact: false }).waitFor()
    for (const width of [1366, 768, 360]) {
      await t.page.setViewportSize({ width, height: 820 })
      assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= width + 1)
      await t.screenshot('customer-outstanding-' + width)
    }
    await t.page.getByLabel('Customer search').fill('missing')
    await t.page.getByRole('button', { name: 'Search / Refresh' }).click()
    await t.page.getByText('No matching unpaid credit invoices.', { exact: true }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS outstanding report filtering and responsive layout')
  }

  {
    const t = await scenario('business-owner-all-tabs', { role: 'Owner', hasProducts: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    assert.equal(await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Cash Register' }).count(), 0,
      'Unreleased cash register must not appear in the owner workspace')
    const modules = [
      ['Products', 'Products', 'BUS-06-products-compact'],
      ['Purchase Orders', 'Purchase Orders', 'BUS-20-purchases-compact'],
      ['Inventory', 'Inventory', 'BUS-27-inventory-compact'],
      ['Product Masters', 'Product Masters', null],
      ['Suppliers', 'Suppliers', null],
      ['Customers', 'Customers', null],
      ['Sales & Returns', 'Sales & Returns', 'BUS-35-posted-sales-compact'],
      ['Reports', 'Reports', null],
      ['Staff & Access', 'Team Access', null],
      ['Business Settings', 'Business Settings', null]
    ]
    for (const [label, heading, shot] of modules) {
      await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: label }).first().click()
      await t.page.getByRole('heading', { name: heading, exact: true, level: 2 }).waitFor()
      assert.equal(await t.page.locator('.business-page-header').count(), 0,
        label + ': duplicated page hero must not render')
      assert.equal(await t.page.locator('.business-module-title').count(), 0,
        label + ': old icon/title/description hero must not render')
      if (await t.page.locator('.business-module-heading').count() > 0) {
        const headerHeight = await t.page.locator('.business-module-heading').first()
          .evaluate(header => header.getBoundingClientRect().height)
        assert.ok(headerHeight >= 63, label + ': common module header should be 64px minimum')
      }
      if (label === 'Sales & Returns') {
        assert.equal(await t.page.getByRole('tab', { name: 'Exchanges' }).count(), 0)
        assert.equal(await t.page.getByText('Under construction', { exact: true }).count(), 0)
        await t.page.route('**/api/businesses/1/cash-sales/business-receipts?*', route => {
          const params = new URL(route.request().url()).searchParams
          assert.equal(params.get('locationId'), '0')
          const matched = params.get('search') === '42' && params.get('fromDate') === '2026-11-23' && params.get('toDate') === '2026-11-23'
          return response(route, 200, { items: matched
            ? [{ id: 42, locationId: 1, locationName: 'Main Shop', customerName: 'Sample Customer',
                paymentMethod: 'Cash', status: 'Partial return', printAvailable: true,
                total: 24, currencyCode: 'INR', postedAtUtc: '2026-11-23T06:00:00Z' }]
            : [], total: matched ? 1 : 0, pageSize: 10, totals: matched ? [{ currencyCode: 'INR', total: 24, cash: 24, credit: 0 }] : [], hasMore: false })
        })
        await t.page.getByLabel('Invoice / customer').fill('42')
        await t.page.getByLabel('From date').fill('2026-11-23')
        await t.page.getByLabel('To date').fill('2026-11-23')
        await t.page.getByRole('button', { name: 'Search', exact: true }).click()
        await t.page.getByRole('row').filter({ hasText: '#42' }).getByText('INR 24.00').waitFor()
        let postedReturn = null
        await t.page.route('**/api/businesses/1/cash-sales/business-receipts/42?*', route => {
          assert.equal(route.request().method(), 'GET')
          assert.equal(new URL(route.request().url()).searchParams.get('locationId'), '1')
          return response(route, 200, { id: 42, total: 24, tendered: 30, currencyCode: 'INR', printAvailable: true,
            postedAtUtc: '2026-11-23T06:00:00Z',
            locationName: 'Main Shop', customerName: 'Sample Customer', customerId: 9,
            lines: [{ id: 1, productName: 'Loose Rice', productSku: 'RICE-KG', unitCode: 'KG',
              unitPrice: 96, quantity: 0.25, lineTotal: 24,
              conversionFactor: 0.4, baseUnitCode: 'BAG',
              returnedQuantity: postedReturn ? 0.175 : 0.05, remainingQuantity: postedReturn ? 0.075 : 0.2,
              returnedAmount: postedReturn ? 16.8 : 4.8, remainingAmount: postedReturn ? 7.2 : 19.2 }] })
        })
        let firstReturnRequest = null
        let returnAttempts = 0
        await t.page.route('**/api/businesses/1/cash-sales/business-receipts/42/returns?*', route => {
          const params = new URL(route.request().url()).searchParams
          assert.equal(params.get('locationId'), '1')
          return response(route, 200, { items: postedReturn ? [postedReturn] : [], hasMore: false })
        })
        await t.page.route('**/api/businesses/1/cash-sales/business-receipts/42/returns', route => {
          assert.equal(route.request().method(), 'POST')
          const request = route.request().postDataJSON()
          assert.equal(request.expectedRefund, 12)
          assert.equal(request.cashRefundConfirmed, true)
          assert.equal(request.lines[0].restock, false)
          returnAttempts++
          if (returnAttempts === 1) {
            firstReturnRequest = request
            postedReturn = { id: 81, saleId: 42, requestKey: request.requestKey, total: 12,
              reason: request.reason, postedAtUtc: '2026-11-23T07:00:00Z',
              lines: [{ saleLineId: 1, quantity: 0.125, amount: 12, restock: false }] }
            return route.abort()
          }
          assert.deepEqual(request, firstReturnRequest)
          return response(route, 200, postedReturn)
        })
        assert.equal(await t.page.getByRole('button', { name: 'View invoice 42' }).count(), 0)
        await t.page.getByRole('button', { name: 'Receipt #42' }).click()
        const receipt = t.page.getByRole('region', { name: 'Cash receipt' })
        const originalLines = receipt.getByRole('table', { name: 'Original posted sale lines' })
        await originalLines.getByText('Loose Rice', { exact: true }).waitFor()
        await originalLines.getByText('RICE-KG', { exact: true }).waitFor()
        await originalLines.getByRole('cell', { name: '0.25 KG', exact: true }).waitFor()
        await originalLines.getByRole('cell', { name: 'INR 24.00', exact: true }).waitFor()
        await receipt.getByText('Sample Customer', { exact: true }).waitFor()
        await receipt.getByText('Main Shop', { exact: true }).waitFor()
        await receipt.getByText(/Change INR 6\.00/).waitFor()
        assert.equal(await receipt.getByRole('link', { name: 'Print receipt' }).getAttribute('href'),
          '/api/businesses/1/cash-sales/business-receipts/42/pdf?locationId=1')
        await t.page.route('**/api/businesses/1/cash-sales/business-receipts/42/return-estimate', route => {
          assert.equal(route.request().method(), 'POST')
          const request = route.request().postDataJSON()
          assert.equal(request.locationId, 1)
          assert.equal(request.lines[0].lineId, 1)
          const qty = request.lines[0].quantity
          return response(route, 200, { receiptId: 42, currencyCode: 'INR', total: qty * 96,
            lines: [{ lineId: 1, quantity: qty, amount: qty * 96 }], canPost: true })
        })
        await receipt.getByRole('button', { name: 'Estimate return' }).click()
        const estimate = receipt.getByRole('region', { name: 'Return estimate' })
        await estimate.getByText('Sold: 0.25 · Returned: 0.05 · Remaining: 0.2 KG', { exact: true }).waitFor()
        await estimate.getByRole('button', { name: 'Select remaining quantity' }).click()
        await estimate.getByRole('button', { name: 'Calculate estimate' }).click()
        await estimate.getByText('Estimated return amount: INR 19.20', { exact: true }).waitFor()
        const quantity = estimate.getByRole('spinbutton', { name: 'Return quantity for Loose Rice' })
        await quantity.fill('0.225')
        assert.equal(await estimate.getByRole('button', { name: 'Calculate estimate' }).isDisabled(), true)
        await quantity.fill('0.001')
        await estimate.getByText('Enter a quantity that converts exactly to three stock decimals.').waitFor()
        assert.equal(await estimate.getByRole('button', { name: 'Calculate estimate' }).isDisabled(), true)
        await quantity.fill('0.125')
        await estimate.getByText('0.05 BAG stock equivalent', { exact: true }).waitFor()
        await estimate.getByRole('button', { name: 'Calculate estimate' }).click()
        await estimate.getByText('Estimated return amount: INR 12.00', { exact: true }).waitFor()
        const record = estimate.getByRole('button', { name: 'Record return', exact: true })
        assert.equal(await record.isDisabled(), true)
        await estimate.getByLabel('Stock disposition — Loose Rice').selectOption('no-stock')
        await estimate.getByLabel('Return reason').fill('Damaged goods')
        await estimate.getByRole('checkbox').check()
        await t.screenshot('BUS-38-return-confirmation-desktop')
        const viewport = t.page.viewportSize()
        for (const width of [768, 360]) {
          await t.page.setViewportSize({ width, height: 800 })
          assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= width + 1,
            'Return confirmation must fit ' + width + 'px without page overflow')
          await t.screenshot('BUS-38-return-confirmation-' + width)
        }
        await t.page.setViewportSize(viewport)
        await record.click()
        const recovery = receipt.getByRole('region', { name: 'Return recovery' })
        await recovery.getByRole('alert').waitFor()
        await recovery.getByRole('button', { name: 'Check posted return' }).click()
        await receipt.getByText(/Return #81 recorded: INR 12.00/).waitFor()
        assert.equal(returnAttempts, 1)
        const history = receipt.getByRole('region', { name: 'Return history' })
        await history.getByRole('button', { name: 'Return #81' }).click()
        await history.getByText(/Not restocked/).waitFor()
        await receipt.getByRole('button', { name: 'Estimate return' }).click()
        await receipt.getByText('Sold: 0.25 · Returned: 0.175 · Remaining: 0.075 KG', { exact: true }).waitFor()
        await receipt.getByRole('button', { name: 'Back to receipts' }).click()
        assert.equal(await t.page.getByLabel('Invoice / customer').inputValue(), '42')
        assert.equal(await t.page.getByLabel('From date').inputValue(), '2026-11-23')
        assert.equal(await t.page.getByLabel('To date').inputValue(), '2026-11-23')
        await t.page.route('**/api/businesses/1/cash-sales/business-returns?*', route => {
          const params = new URL(route.request().url()).searchParams
          const matched = params.get('search') !== 'missing'
          return response(route, 200, { total: matched ? 1 : 0, pageSize: 10, hasMore: false,
            totals: matched ? [{ currencyCode: 'INR', refundTotal: 12, returnCount: 1 }] : [],
            items: matched ? [{ id: 81, saleId: 42, locationId: 1, locationName: 'Main Shop',
              customerName: 'Sample Customer', currencyCode: 'INR', total: 12, reason: 'Damaged goods',
              postedAtUtc: '2026-11-23T07:00:00Z', lines: [{ id: 1, productName: 'Loose Rice',
                productSku: 'RICE-KG', quantity: 0.125, unitCode: 'KG', amount: 12, restock: false }] }] : [] })
        })
        await t.page.getByRole('tab', { name: 'Returns', exact: true }).click()
        await t.page.getByRole('button', { name: 'Return #81' }).click()
        await t.page.getByRole('table', { name: 'Return 81 lines' }).getByRole('cell', { name: /Loose Rice.*RICE-KG/ }).waitFor()
        await t.page.getByRole('table', { name: 'Return 81 lines' }).getByText('Not restocked', { exact: true }).waitFor()
        await t.page.getByRole('button', { name: 'Invoice #42' }).click()
        await t.page.getByRole('region', { name: 'Cash receipt' }).getByText('Sample Customer', { exact: true }).waitFor()
        await t.page.getByRole('button', { name: 'Back to returns' }).click()
        const originalViewport = t.page.viewportSize()
        for (const width of [1366, 768, 360]) {
          await t.page.setViewportSize({ width, height: 820 })
          assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= width + 1)
          await t.screenshot('BUS-38-returns-register-' + width)
        }
        await t.page.setViewportSize(originalViewport)
        await t.page.getByLabel('Return / invoice / customer').fill('missing')
        await t.page.getByRole('button', { name: 'Search', exact: true }).click()
        await t.page.getByText('No matching posted returns.', { exact: true }).waitFor()
        await t.page.getByRole('button', { name: 'Clear filters' }).click()
        await t.page.getByRole('button', { name: 'Return #81' }).waitFor()
        await t.page.getByRole('tab', { name: 'Invoices', exact: true }).click()
      }
      if (label === 'Reports') {
        assert.equal(await t.page.getByRole('tab').count(), 2, 'Only released reports should be shown')
        await t.page.route('**/api/businesses/1/cash-sales/daily-report?*', route => {
          const params = new URL(route.request().url()).searchParams
          assert.equal(params.get('locationId'), '1')
          return response(route, 200, { date: params.get('date'), timeZoneId: 'Asia/Kolkata',
            totals: params.get('date') === '2026-11-23'
              ? [{ currencyCode: 'INR', receiptCount: 1, salesTotal: 24, returnCount: 1, refundTotal: 4.8, netSales: 19.2 }] : [] })
        })
        await t.page.getByLabel('Business date').fill('2026-11-22')
        await t.page.getByText('No posted cash receipts or returns for this date and location.').waitFor()
        await t.page.getByLabel('Business date').fill('2026-11-23')
        const reportRow = t.page.getByRole('row').filter({ hasText: 'INR' })
        await reportRow.getByText('24.00', { exact: true }).waitFor()
        await reportRow.getByText('4.80', { exact: true }).waitFor()
        await reportRow.getByText('19.20', { exact: true }).waitFor()
      }
      if (shot) await t.screenshot(shot)
    }
    await t.check()
    await t.context.close()
    console.log('PASS owner released tabs keep one compact title')
  }

  {
    const t = await scenario('business-module-mobile', {
      role: 'Owner', hasProducts: true, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await t.page.getByRole('button', { name: 'Open navigation' }).click()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).first().click()
    await t.page.getByRole('heading', { name: 'Products', exact: true, level: 2 }).waitFor()
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Products tab must fit 360px viewport without horizontal overflow')
    await t.screenshot('BUS-06-products-compact-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS mobile compact business module layout')
  }

  {
    // Managers may operate the store, but never receive owner financial KPIs
    // or owner-only navigation such as staff management and subscriptions.
    const t = await scenario('manager-role-boundary', {
      role: 'Manager', hasProducts: true, pendingApprovals: 3,
      cashSalesFixture: true, stockAlertsFixture: true
    })
    // Defense-in-depth: a malformed response must not expose owner receipts.
    await t.page.route('**/api/businesses/1/dashboard', route => {
      const malformed = dashboard('Owner', true, 3, 1, 1, true, true)
      malformed.roleCode = 'Manager'
      return response(route, 200, malformed)
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    assert.equal(await t.page.locator('.business-kpi-card').count(), 0,
      'Manager must not see owner-only financial and approval KPI cards')
    assert.equal(await t.page.getByRole('heading', { name: 'Recent Activity' }).count(), 0,
      'Manager must not see owner-only receipt totals')
    assert.equal(await t.page.getByRole('button', { name: 'Staff & Access' }).count(), 0)
    assert.equal(await t.page.getByRole('button', { name: 'Business Settings' }).count(), 0)
    assert.equal(await t.page.getByRole('button', { name: 'Sales & Returns' }).count(), 0)
    assert.equal(await t.page.getByRole('button', { name: 'Reports', exact: true }).count(), 0)
    assert.equal(await t.page.getByText('Subscription & billing').count(), 0)
    assert.equal(await t.page.getByRole('button', { name: /Open POS Checkout/ }).count(), 0,
      'POS must only be visible when enabled by the API')
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    await t.page.getByRole('heading', { name: 'Inventory', level: 2 }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS Manager owner-only KPI, navigation and subscription isolation')
  }

  {
    const t = await scenario('multi-business', { role: 'Manager', multiple: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    assert.equal(await t.page.locator('.business-row').count(), 2)
    assert.equal(await t.page.getByText('Select an assigned business to continue.').count(), 0,
      'Business selector must not repeat obvious instructions')
    assert.equal(await t.page.getByRole('button', { name: 'Open Sample Retail as Manager' }).count(), 1,
      'Business choice must have an accessible name and role')
    await t.screenshot('multi-business-picker')
    const firstRowHeight = await t.page.locator('.business-row').first().evaluate(el => el.getBoundingClientRect().height)
    assert.ok(firstRowHeight <= 85, 'Business selector rows should stay compact')
    assert.equal(await t.page.getByRole('button', { name: /Create Business/i }).count(), 0, 'Owner/Manager cannot provision tenants here')
    await t.page.locator('.business-row').nth(1).click()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    await t.page.locator('.workspace-identity strong').getByText('Second Retail').waitFor()
    await t.page.getByRole('button', { name: 'Switch Business' }).click()
    await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    await t.page.locator('.business-row').first().click()
    await t.page.locator('.workspace-identity strong').getByText('Sample Retail').waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS multi-business select / manager workspace')
  }

  {
    const t = await scenario('multi-business-mobile', { role: 'Owner', multiple: true, width: 360, height: 780 })
    await t.login()
    await t.page.getByRole('heading', { name: 'Choose a business' }).waitFor()
    assert.equal(await t.page.locator('.business-row').count(), 2)
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'BUS-03 chooser must fit at 360px')
    await t.screenshot('BUS-03-business-selector-mobile-360')
    await t.page.locator('.business-row').nth(1).click()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    assert.equal((await t.page.locator('.workspace-identity strong').textContent())?.trim(), 'Second Retail')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-03 responsive business selector and tenant navigation')
  }

  {
    const t = await scenario('BUS-04-one-location', { role: 'Owner', locationCount: 1 })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    await t.page.getByRole('heading', { name: 'Inventory', level: 2 }).waitFor()
    assert.equal(await t.page.locator('#stock-location').count(), 0,
      'Single location must not show an unnecessary dropdown')
    await t.page.getByText('Main Shop', { exact: true }).last().waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS BUS-04 single assigned location shown without redundant selector')
  }

  {
    const t = await scenario('BUS-04-two-locations', {
      role: 'Owner', locationCount: 2, hasProducts: true, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Dashboard', exact: true }).waitFor()
    await t.page.getByRole('button', { name: 'Open navigation' }).click()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    await t.page.getByRole('heading', { name: 'Inventory', level: 2 }).waitFor()
    assert.equal(await t.page.locator('.workspace-identity').getByText('2 locations').count(), 1,
      'Multi-branch role context must not claim the first branch is active')
    await t.page.getByLabel('Stock location').selectOption('2')
    await t.page.waitForFunction(() => document.querySelector('#stock-location')?.value === '2')
    assert.ok(t.stockPaths.some(path => path.endsWith('/locations/2')),
      'Stock fetch must use selected authorized location')
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Location selector must fit 360px')
    await t.screenshot('BUS-04-stock-location-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-04 authorized multi-location stock scope mobile')
  }

  {
    const t = await scenario('BUS-06-products-real-fixture', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    await t.login()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).first().click()
    await t.page.getByRole('heading', { name: 'Products', level: 2 }).waitFor()
    await t.page.getByText('Blue Shirt').waitFor()
    await t.page.getByText('Red Bag').waitFor()
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 2)
    assert.equal(await t.page.locator('.masters-pages').count(), 0,
      'Do not display a pagination bar when all products fit one page')
    assert.equal(await t.page.locator('#product-filter-subgroup').count(), 0,
      'Do not render subgroup before a group is selected')
    assert.equal(await t.page.locator('#product-filter-category').count(), 0,
      'Do not render category before a subgroup is selected')
    await t.screenshot('BUS-06-products-list-desktop')
    await t.page.locator('#product-filter-status').selectOption('inactive')
    await t.page.waitForFunction(() => {
      const rows = document.querySelectorAll('.business-record-table tbody tr')
      return rows.length === 1 && rows[0].textContent.includes('Red Bag')
    })
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 1,
      'Inactive status must filter actual records')
    await t.page.locator('#product-filter-status').selectOption('all')
    await t.page.locator('#product-filter-group').selectOption('2')
    await t.page.waitForFunction(() => {
      const rows = document.querySelectorAll('.business-record-table tbody tr')
      return rows.length === 1 && rows[0].textContent.includes('Blue Shirt')
    })
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 1,
      'Category selection must filter records')
    assert.equal(await t.page.locator('#product-filter-subgroup').count(), 1,
      'Subgroup selection becomes available after group selection')
    await t.page.locator('#product-filter-group').selectOption('0')
    assert.equal(await t.page.locator('#product-filter-subgroup').count(), 0)
    await t.page.locator('#product-search').fill('BLUE-11')
    await t.page.getByRole('button', { name: 'Search', exact: true }).click()
    await t.page.waitForFunction(() => {
      const rows = document.querySelectorAll('.business-record-table tbody tr')
      return rows.length === 1 && rows[0].textContent.includes('Blue Shirt')
    })
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 1,
      'SKU search must filter server results')
    await t.page.getByRole('button', { name: 'Add Product' }).click()
    await t.page.getByRole('heading', { name: 'New product' }).waitFor()
    await t.page.getByRole('button', { name: 'Cancel' }).click()
    await t.page.getByRole('heading', { name: 'Products', level: 2 }).waitFor()
    await t.check()
    await t.context.close()
    console.log('PASS BUS-06 product list, filters, search, create/cancel and truthful pagination')
  }

  {
    const t = await scenario('BUS-06-products-mobile-fixture', {
      role: 'Owner', hasProducts: true, productsFixture: true, width: 360, height: 780
    })
    await t.login()
    await t.page.getByRole('button', { name: 'Open navigation' }).click()
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Products' }).first().click()
    await t.page.getByText('Blue Shirt').waitFor()
    assert.ok(await t.page.evaluate(() => document.documentElement.scrollWidth) <= 361,
      'Products listing should not overflow the 360px page')
    assert.equal(await t.page.getByRole('columnheader', { name: 'Sale price' }).isVisible(), true,
      'Sale price must remain visible without horizontal swiping')
    assert.equal(await t.page.getByRole('columnheader', { name: 'Action' }).isVisible(), true,
      'Edit action must remain visible without horizontal swiping')
    assert.equal(await t.page.getByRole('columnheader', { name: 'SKU / barcode' }).isVisible(), false,
      'SKU moves below product name on mobile')
    const filterWidth = await t.page.locator('#product-filter-status').evaluate(el => el.getBoundingClientRect().width)
    assert.ok(filterWidth > 245, 'Status filter must remain readable at 360px')
    assert.equal(await t.page.getByText('Active', { exact: true }).count() > 0, true)
    await t.screenshot('BUS-06-products-list-mobile-360')
    await t.check()
    await t.context.close()
    console.log('PASS BUS-06 product list at 360px mobile')
  }

  {
    const t = await scenario('cashier', { role: 'Cashier' })
    await t.login()
    await t.page.locator('.pos-shell').waitFor()
    assert.equal(await t.page.locator('.pos-topbar-brand small').evaluate(el =>
      getComputedStyle(el).color), 'rgb(255, 255, 255)',
      'POS dark header labels remain pure white')
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
    assert.equal(await t.page.locator('.platform-shell .app-sidebar .nav-entry').first()
      .evaluate(el => getComputedStyle(el).color), 'rgb(255, 255, 255)',
      'Super Admin dark sidebar labels remain pure white')
    const platformCardHeights = await t.page.locator('.platform-metric-grid > .platform-metric')
      .evaluateAll(cards => cards.map(card => card.getBoundingClientRect().height))
    if (platformCardHeights.length > 1)
      assert.ok(Math.max(...platformCardHeights) - Math.min(...platformCardHeights) <= 1,
        'Super Admin metric tiles in the same row must align')
    await t.screenshot('platform-super-admin-workspace')
    await t.check()
    await t.context.close()
    console.log('PASS Super Admin destination')
  }

  {
    // Draft payment badges must come from the joined server record, not
    // only from the first page of the independent payment-request register.
    const t = await scenario('super-admin-billing-status-and-pagination', { role: 'Platform' })
    const requestPages = [], capturePages = []
    await t.page.route('**/api/platform/plans', route =>
      response(route, 200, { items: [], unknownPlanAssignments: 0 }))
    await t.page.route('**/api/platform/billing/drafts?*', route =>
      response(route, 200, {
        items: [{
          id: 7, businessId: 11, businessName: 'Paid Retail', publishedPlanVersionId: 5,
          planCode: 'STARTER', billingCycle: 'Monthly', periodStartDate: '2026-11-01',
          periodEndDate: '2026-12-01', currencyCode: 'INR', amount: 1099,
          createdAtUtc: '2026-11-01T00:00:00Z', status: 'Draft',
          paymentRequestId: 900, paymentStatus: 'Captured'
        }], total: 1, page: 1, pageSize: 20
      }))
    await t.page.route('**/api/platform/billing/payment-requests?*', route => {
      const page = Number(new URL(route.request().url()).searchParams.get('page') ?? '1')
      requestPages.push(page)
      return response(route, 200, {
        // Request #900 is not in the first page, so the first page cannot be
        // used to decide whether billing draft #7 has already been prepared.
        items: page === 1 ? Array.from({ length: 20 }, (_, i) => ({
          id: 100 + i, billingDraftId: 50 + i, businessId: 12,
          businessName: 'Other Retail', currencyCode: 'INR', amount: 100,
          status: 'AwaitingGateway'
        })) : [{
          id: 900, billingDraftId: 7, businessId: 11,
          businessName: 'Paid Retail', currencyCode: 'INR', amount: 1099,
          status: 'Captured'
        }],
        total: 21, page, pageSize: 20
      })
    })
    await t.page.route('**/api/platform/billing/reconciliation?*', route => {
      const page = Number(new URL(route.request().url()).searchParams.get('page') ?? '1')
      capturePages.push(page)
      return response(route, 200, {
        items: [{ id: page, paymentRequestId: 900,
          providerPaymentId: 'pay_test_verified', businessId: 11,
          businessName: 'Paid Retail', amount: 1099, currencyCode: 'INR',
          refundedAmount: 0, netAmount: 1099, activationStatus: 'Activated' }],
        total: 21, page, pageSize: 20, unresolvedOrders: []
      })
    })
    await t.login()
    await t.page.locator('.platform-shell .app-sidebar .nav-entry')
      .filter({ hasText: 'Plans & Billing' }).click()
    await t.page.getByRole('heading', { name: 'Plans & Billing' }).waitFor()
    await t.page.getByRole('tab', { name: 'Billing Drafts' }).click()
    await t.page.getByRole('heading', { name: 'Billing Draft History' }).waitFor()
    await t.page.getByText('Captured — verified').waitFor()
    await t.page.getByText('Request #900').waitFor()
    assert.equal(await t.page.getByRole('button', { name: 'Prepare Payment', exact: true }).count(),
      0, 'Existing captured draft may never expose a duplicate payment preparation action')
    await t.page.getByRole('button', { name: 'Next requests' }).click()
    await t.page.getByText('21 payment requests · Page 2').waitFor()
    assert.deepEqual(requestPages.slice(-2), [1, 2])
    await t.page.getByRole('button', { name: 'Next captures' }).click()
    await t.page.getByText('Page 2.', { exact: false }).waitFor()
    assert.deepEqual(capturePages.slice(-2), [1, 2])
    await t.screenshot('SA-billing-captured-status-pagination')
    await t.check()
    await t.context.close()
    console.log('PASS billing paid status and independent payment/reconciliation pagination')
  }

  {
    const t = await scenario('platform-live-release-maintenance-and-notices', { role: 'Platform' })
    let typeState = 'Available', typeRevision = 0
    let moduleState = 'Available', moduleRevision = 0
    const maintenance = [], announcements = []
    let policyWrites = 0
    await t.page.route('**/api/platform/business-type-controls', route =>
      response(route, 200, [{
        id: 1, code: 'RETAIL', name: 'General Retail Shop', isSupported: true,
        releaseState: typeState, releaseRevision: typeRevision,
        canProvision: typeState === 'Available'
      }]))
    await t.page.route('**/api/platform/business-type-controls/RETAIL/release', route => {
      const input = route.request().postDataJSON()
      assert.equal(input.expectedRevision, typeRevision)
      assert.equal(input.reason, 'Pause new retail signups during upgrade')
      typeState = input.state
      typeRevision++
      policyWrites++
      return response(route, 200, { releaseState: typeState, releaseRevision: typeRevision })
    })
    await t.page.route('**/api/platform/module-controls', route =>
      response(route, 200, [{
        code: 'CATALOG', name: 'Catalog & Products', installed: true,
        releaseState: moduleState, revision: moduleRevision, canOperate: moduleState === 'Available'
      }, {
        code: 'POS', name: 'POS Cash Sales', installed: false,
        releaseState: 'Under Construction', revision: 0, canOperate: false
      }]))
    await t.page.route('**/api/platform/module-controls/CATALOG/release', route => {
      const input = route.request().postDataJSON()
      assert.equal(input.expectedRevision, moduleRevision)
      assert.equal(input.reason, 'Pause catalog writes for coordinated release')
      moduleState = input.state
      moduleRevision++
      policyWrites++
      return response(route, 200, { releaseState: moduleState, revision: moduleRevision })
    })
    await t.page.route('**/api/platform/maintenance', route => {
      if (route.request().method() === 'GET') return response(route, 200, maintenance)
      const input = route.request().postDataJSON()
      assert.equal(input.mode, 'ReadOnly')
      assert.ok(input.startUtc.endsWith('Z'), 'Maintenance schedule must use UTC')
      assert.ok(Date.parse(input.endUtc) > Date.parse(input.startUtc))
      maintenance.push({
        id: 91, ...input, isCancelled: false, createdAtUtc: new Date().toISOString()
      })
      return response(route, 201, maintenance.at(-1))
    })
    await t.page.route('**/api/platform/announcements', route => {
      if (route.request().method() === 'GET') return response(route, 200, announcements)
      const input = route.request().postDataJSON()
      assert.equal(input.level, 'Warning')
      announcements.push({
        id: 92, ...input, isArchived: false, createdAtUtc: new Date().toISOString()
      })
      return response(route, 201, announcements.at(-1))
    })
    await t.page.route('**/api/public/platform-availability', route =>
      response(route, 200, {
        serverTimeUtc: new Date().toISOString(),
        maintenance: maintenance[0] ?? null,
        announcements
      }))
    await t.login()
    await t.page.locator('.platform-shell .app-sidebar .nav-entry')
      .filter({ hasText: 'Platform Settings' }).click()
    await t.page.getByRole('tab', { name: 'Business Types' }).click()
    await t.page.locator('#release-type-status').selectOption('Paused')
    await t.page.locator('#release-type-reason').fill('Pause new retail signups during upgrade')
    await t.page.getByRole('button', { name: 'Apply release policy' }).click()
    await t.page.getByText('release policy saved and audited.', { exact: false }).waitFor()
    assert.equal(typeState, 'Paused')
    await t.page.getByRole('tab', { name: 'Modules & Access' }).click()
    await t.page.locator('#release-module-status').selectOption('Paused')
    await t.page.locator('#release-module-reason').fill('Pause catalog writes for coordinated release')
    await t.page.getByRole('button', { name: 'Apply module policy' }).click()
    await t.page.getByText('New writes now follow the published state.', { exact: false }).waitFor()
    assert.equal(policyWrites, 2)
    await t.page.getByRole('tab', { name: 'Maintenance' }).click()
    await t.page.locator('#maintenance-mode').selectOption('ReadOnly')
    const local = date => new Date(date.getTime() - date.getTimezoneOffset() * 60000)
      .toISOString().slice(0, 16)
    await t.page.locator('#maintenance-start').fill(local(new Date(Date.now() + 5 * 60000)))
    await t.page.locator('#maintenance-end').fill(local(new Date(Date.now() + 35 * 60000)))
    await t.page.locator('#maintenance-reason').fill('Scheduled read-only update approved by operator')
    await t.page.getByRole('button', { name: 'Schedule maintenance' }).click()
    await t.page.getByText('Maintenance window scheduled and audited.', { exact: false }).waitFor()
    assert.equal(maintenance.length, 1)
    await t.page.getByRole('tab', { name: 'Announcements' }).click()
    await t.page.locator('#notice-severity').selectOption('Warning')
    await t.page.locator('#notice-start').fill(local(new Date(Date.now() + 5 * 60000)))
    await t.page.locator('#notice-end').fill(local(new Date(Date.now() + 35 * 60000)))
    await t.page.locator('#notice-reason').fill('Publish visitor update after operator review')
    await t.page.getByRole('button', { name: 'Publish announcement' }).click()
    await t.page.getByText('Announcement published with its scheduled window.', { exact: false }).waitFor()
    assert.equal(announcements.length, 1)
    await t.screenshot('SA-33-platform-controls')
    await t.check()
    await t.context.close()
    console.log('PASS Super Admin live type, module, maintenance and notice controls')
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

  for (const width of [1366, 360]) {
    const setup = await scenario('opening-stock-setup-' + width, { role: 'Owner', width })
    let hasInitializedStock = false
    await setup.page.route('**/api/businesses/1/dashboard', route => response(route, 200, {
      ...dashboard('Owner', true), hasInitializedStock
    }))
    await setup.login()
    const checklist = setup.page.getByRole('region', { name: 'Complete store setup' })
    await checklist.getByText('First product added', { exact: true }).waitFor()
    await checklist.getByRole('button', { name: 'Record opening stock' }).click()
    await setup.page.getByRole('heading', { name: 'Inventory', exact: true }).waitFor()
    hasInitializedStock = true
    if (width === 360) await setup.page.getByRole('button', { name: 'Open navigation' }).click()
    await setup.page.locator('.app-sidebar').getByRole('button', { name: 'Dashboard', exact: true }).click()
    await setup.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    await checklist.waitFor({ state: 'hidden' })
    await setup.check()
    await setup.context.close()
    console.log('PASS BUS-02 opening stock setup and dashboard refresh at ' + width + 'px')
  }

  for (const width of [1366, 360]) {
    const t = await scenario('provisioning-eligibility-' + width, { role: 'Platform', width })
    const block = {
      code: 'MODULE_NOT_AVAILABLE',
      message: 'Required module CATALOG is not available for new businesses.'
    }
    let eligible = false
    let trialDays = 14
    await t.page.route('**/api/platform/provisioning-eligibility', route => response(route, 200, {
      types: eligible ? [{ id: 1, code: 'RETAIL', name: 'Retail' }] : [],
      block: eligible ? null : block,
      trialDays
    }))
    await t.page.route('**/api/business-markets', route => response(route, 200, [
      { code: 'IN', name: 'India', currencies: ['INR'], timeZones: ['Asia/Kolkata'] }
    ]))
    await t.login()
    {
      await t.page.route('**/api/platform/business-type-controls', route => response(route, 200, [
        { id: 1, code: 'RETAIL', name: 'Retail' }
      ]))
      await t.page.route('**/api/platform/businesses?*', route => {
        const query = new URL(route.request().url()).searchParams
        const matches = query.get('ownerEmail') === 'owner@example.test' &&
          query.get('businessTypeId') === '1' && query.get('page') === '1'
        const items = matches ? [{
          id: 42, name: 'Matching Retail', ownerEmail: 'owner@example.test',
          typeName: 'Retail', countryCode: 'IN', planCode: 'STARTER', directoryStatus: 'Trial',
          createdAtUtc: '2026-11-23T23:45:00Z'
        }] : []
        return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
      })
      const statusFilter = width === 1366 ? 'Trial' : 'TrialExpired'
      const trialDirectoryRequest = t.page.waitForRequest(request => {
        const url = new URL(request.url())
        return url.pathname === '/api/platform/businesses' && url.searchParams.get('status') === statusFilter
      })
      await t.page.getByRole('button', { name: width === 1366 ? 'View active trials' : 'View expired trials' }).click()
      await trialDirectoryRequest
      assert.equal(await t.page.getByLabel('Filter by subscription status').inputValue(), statusFilter)
      await t.page.getByRole('button', { name: 'Clear', exact: true }).click()
      await t.page.getByLabel('Filter by owner email').fill('owner@example.test')
      await t.page.getByLabel('Filter by business type').selectOption('1')
      await t.page.getByRole('button', { name: 'Search', exact: true }).click()
      const row = t.page.getByRole('row').filter({ hasText: 'Matching Retail' })
      await t.page.getByRole('columnheader', { name: 'Created (UTC)' }).waitFor()
      const created = row.locator('time')
      assert.equal(await created.getAttribute('datetime'), '2026-11-23T23:45:00Z')
      assert.equal(await created.textContent(), await t.page.evaluate(() =>
        new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'UTC' })
          .format(new Date('2026-11-23T23:45:00Z'))))
      await created.scrollIntoViewIfNeeded()
      await t.screenshot('SA-03-directory-created-' + width)
      await t.page.route('**/api/platform/businesses/42', route => response(route, 200, {
        id: 42, name: 'Matching Retail', ownerEmail: 'owner@example.test', typeName: 'Retail',
        countryCode: 'IN', currencyCode: 'INR', timeZoneId: 'Asia/Kolkata', locationCount: 2,
        primaryLocationName: 'Main Shop', planCode: 'STARTER', subscriptionStatus: 'Active',
        accessStatus: 'Inactive', paidPlanVersionId: 17, paidPlanVersion: 2,
        paidStartsAtUtc: '2026-10-23T23:45:00Z', paidEndsAtUtc: '2026-11-23T23:45:00Z'
      }))
      await row.getByRole('button', { name: 'View details for Matching Retail' }).click()
      const details = t.page.getByRole('region', { name: 'Business details' })
      await details.getByText('Main Shop', { exact: true }).waitFor()
      for (const [label, value] of [
        ['Locations', '2'], ['Paid plan version', '2'], ['Subscription', 'Inactive']
      ]) {
        const field = details.locator('dl > div').filter({ has: t.page.getByText(label, { exact: true }) })
        assert.equal(await field.locator('dd').textContent(), value)
      }
      const paidEnd = details.locator('dl > div').filter({ hasText: 'Paid period ends' }).locator('dd')
      assert.equal(await paidEnd.textContent(), await t.page.evaluate(() =>
        new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Kolkata' })
          .format(new Date('2026-11-23T23:45:00Z'))))
      await t.screenshot('SA-09-business-details-' + width)
      const reasonForStatusChange = 'Confirmed subscription status review'
      const statusChanges = []
      await t.page.route('**/api/platform/businesses/42/subscription-status', route => {
        statusChanges.push(route.request().postDataJSON())
        return statusChanges.length === 1
          ? response(route, 200, { status: 'Suspended', accessStatus: 'Suspended', auditId: 71 })
          : response(route, 200, { status: 'Active', accessStatus: 'Inactive', auditId: 72 })
      })
      for (const action of ['Suspend', 'Reactivate']) {
        await t.page.getByRole('button', { name: action + ' Subscription', exact: true }).click()
        const change = t.page.getByRole('region', { name: 'Confirm subscription status change' })
        await change.getByLabel(/Reason for/).fill(reasonForStatusChange)
        await change.getByRole('button', { name: 'Confirm ' + action }).click()
        await t.page.getByRole('status').filter({ hasText: action === 'Suspend'
          ? 'Subscription suspended. Audit event #71'
          : 'Suspension lifted. Business access remains unavailable outside the valid subscription period. Audit event #72' }).waitFor()
      }
      assert.deepEqual(statusChanges, [
        { action: 'Suspend', expectedStatus: 'Active', reason: reasonForStatusChange },
        { action: 'Reactivate', expectedStatus: 'Suspended', reason: reasonForStatusChange }
      ])
      await t.page.getByRole('button', { name: 'All businesses' }).click()
      const trialEnd = new Date(Date.now() + 86400000).toISOString()
      await t.page.route('**/api/platform/businesses/42', route => response(route, 200, {
        id: 42, name: 'Matching Retail', timeZoneId: 'Asia/Kolkata', locationCount: 2,
        subscriptionStatus: 'Trial', accessStatus: 'Trial', trialEndsAtUtc: trialEnd
      }))
      await row.getByRole('button', { name: 'View details for Matching Retail' }).click()
      await t.page.getByRole('button', { name: 'Extend Trial', exact: true }).click()
      const extension = t.page.getByRole('region', { name: 'Extend trial' })
      await extension.getByText(/Current expiry is .*Asia\/Kolkata/).waitFor()
      await extension.getByText(/Estimated new expiry is .*Asia\/Kolkata/).waitFor()
      await extension.getByLabel('Additional days').fill('31')
      assert.equal(await extension.getByText(/Estimated new expiry is/).count(), 0)
      const confirmExtension = extension.getByRole('button', { name: 'Confirm Trial Extension' })
      const reason = 'Approved extension for store setup'
      await extension.getByLabel('Reason for extension').fill(reason)
      assert.equal(await confirmExtension.isDisabled(), true)
      await extension.getByLabel('Additional days').fill('2.5')
      assert.equal(await confirmExtension.isDisabled(), true)
      await extension.getByLabel('Additional days').fill('2')
      const confirmedExpiry = new Date(Date.parse(trialEnd) + 2 * 86400000).toISOString()
      let extensionRequest
      await t.page.route('**/api/platform/businesses/42/extend-trial', route => {
        extensionRequest = route.request().postDataJSON()
        return response(route, 200, { trialEndsAtUtc: confirmedExpiry, auditId: 73 })
      })
      await confirmExtension.click()
      await t.page.getByRole('status').filter({ hasText: 'Audit event #73 recorded.' }).waitFor()
      assert.deepEqual(extensionRequest, { additionalDays: 2, expectedTrialEndsAtUtc: trialEnd, reason })
      const trialExpiry = details.locator('dl > div').filter({ hasText: 'Trial end' }).locator('dd')
      assert.equal(await trialExpiry.textContent(), await t.page.evaluate(date =>
        new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Kolkata' })
          .format(new Date(date)), confirmedExpiry))
      await t.page.route('**/api/platform/audit?*', route => {
        const params = new URL(route.request().url()).searchParams
        const event = {
          id: 73, actorUserId: 10, action: 'Subscription.TrialExtended', targetType: 'Business',
          targetId: 42, reason, previousValue: trialEnd, newValue: confirmedExpiry,
          createdAtUtc: '2026-11-23T12:00:00Z'
        }
        const items = [{ ...event, id: 74, actorUserId: 11, action: 'PlatformOwner.SignIn' }, event]
          .filter(item => (!params.has('eventId') || params.get('eventId') === String(item.id)) &&
            (!params.has('actorUserId') || params.get('actorUserId') === String(item.actorUserId)))
        return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
      })
      await t.page.getByRole('button', { name: 'View audit event' }).click()
      await t.page.getByText('Showing audit event #73').waitFor()
      const auditRow = t.page.getByRole('row').filter({ hasText: 'Event #73' })
      await auditRow.getByText('View details', { exact: true }).click()
      await auditRow.getByText(reason, { exact: true }).waitFor()
      assert.equal(await t.page.getByText('Event #74', { exact: true }).count(), 0)
      await t.page.getByRole('button', { name: 'Show all events' }).click()
      await t.page.getByText('Event #74', { exact: true }).waitFor()
      await t.page.getByRole('button', { name: 'Filter by actor #10', exact: true }).click()
      await t.page.getByText('Event #74', { exact: true }).waitFor({ state: 'hidden' })
      await t.page.getByText('Event #73', { exact: true }).waitFor()
      await t.page.getByRole('button', { name: 'Clear actor #10', exact: true }).click()
      await t.page.getByText('Event #74', { exact: true }).waitFor()
      const lookup = t.page.getByLabel('Audit event ID')
      await lookup.fill('7.3')
      assert.equal(await t.page.getByRole('button', { name: 'Find event' }).isDisabled(), true)
      await lookup.fill('73')
      await lookup.press('Enter')
      await t.page.getByText('Showing audit event #73').waitFor()
      await t.page.getByText('Event #73', { exact: true }).waitFor()
      assert.equal(await t.page.getByText('Event #74', { exact: true }).count(), 0)
      console.log('PASS SA-41 exact audit event, actor filter and full history at ' + width + 'px')
      if (width === 360) await t.page.getByRole('button', { name: 'Open navigation' }).click()
      await t.page.locator('.platform-shell .app-sidebar').getByRole('button', { name: 'Businesses', exact: true }).click()
      await t.page.getByRole('heading', { name: 'No businesses created yet' }).waitFor()
      console.log('PASS SA-03 combined filters, UTC creation date and clear at ' + width + 'px')
      await t.page.route('**/api/platform/customers?*', route => {
        const params = new URL(route.request().url()).searchParams
        const items = [
          { id: 20, email: 'owner@example.test', emailConfirmed: true, ownedBusinessCount: 1 },
          { id: 21, email: 'pending@example.test', emailConfirmed: false, ownedBusinessCount: 0 }
        ].filter(item => !params.has('emailConfirmed') ||
          params.get('emailConfirmed') === String(item.emailConfirmed))
        return response(route, 200, { items, total: items.length, page: 1, pageSize: 20 })
      })
      await t.page.route('**/api/platform/customers/20', route => response(route, 200, {
        id: 20, email: 'owner@example.test', emailConfirmed: true,
        businesses: [{ id: 42, name: 'Matching Retail', typeName: 'Retail', planCode: 'STARTER',
          subscriptionStatus: 'Active', accessStatus: 'Inactive' }]
      }))
      if (width === 360) await t.page.getByRole('button', { name: 'Open navigation' }).click()
      await t.page.locator('.platform-shell .app-sidebar').getByRole('button', { name: 'Customers', exact: true }).click()
      const verification = t.page.getByLabel('Filter by email verification')
      await verification.selectOption('false')
      await t.page.getByRole('button', { name: 'View customer pending@example.test' }).waitFor()
      await t.page.getByRole('button', { name: 'View customer owner@example.test' }).waitFor({ state: 'hidden' })
      await verification.selectOption('true')
      await t.page.getByRole('button', { name: 'View customer owner@example.test' }).click()
      await t.page.getByRole('heading', { name: 'Customer Details' }).waitFor()
      const ownedBusiness = t.page.getByRole('row').filter({ hasText: 'Matching Retail' })
      await ownedBusiness.getByRole('cell', { name: 'Inactive', exact: true }).waitFor()
      assert.equal(await ownedBusiness.getByRole('cell', { name: 'Active', exact: true }).count(), 0)
      await ownedBusiness.getByRole('button', { name: 'View Business' }).click()
      await t.page.getByRole('heading', { name: 'Matching Retail', exact: true }).waitFor()
      await t.page.getByRole('button', { name: 'All businesses' }).click()
      console.log('PASS SA-14/SA-15 customer filter and owned business navigation at ' + width + 'px')
    }
    await t.page.getByRole('button', { name: 'Create Business', exact: true }).click()
    await t.page.getByRole('alert').filter({ hasText: block.message }).waitFor()
    assert.equal(await t.page.getByLabel('Business Type').count(), 0)
    await t.screenshot('SA-07-eligibility-blocked-' + width)

    eligible = true
    await t.page.getByRole('button', { name: 'Retry', exact: true }).click()
    await t.page.getByLabel('Business Name').fill('QA Retail Shop')
    await t.page.getByLabel('Business Type').selectOption('1')
    await t.page.getByLabel('Country').selectOption('IN')
    assert.equal(await t.page.getByLabel('Currency').inputValue(), 'INR')
    assert.equal(await t.page.getByLabel('Timezone').inputValue(), 'Asia/Kolkata')
    await t.page.getByRole('button', { name: 'Next: Owner' }).click()
    await t.page.getByRole('heading', { name: 'Owner & Access' }).waitFor()
    if (width === 1366) {
      const ownerEmail = 'owner@example.test'
      const submissions = []
      let emailAccepted = false
      await t.page.route('**/api/platform/owner-lookup?*', route => {
        const email = new URL(route.request().url()).searchParams.get('email')
        if (email === 'new-owner@example.test' && !emailAccepted)
          return response(route, 404, { message: 'Owner account not found.' })
        return response(route, 200, { id: 20, email: email ?? ownerEmail, verified: true })
      })
      let inviteAttempt = 0
      await t.page.route('**/api/platform/owner-invitations', route => {
        const sent = route.request().postDataJSON()
        assert.equal(sent.email, 'new-owner@example.test')
        assert.equal(sent.reason, 'Approved new store owner invitation')
        inviteAttempt++
        return inviteAttempt === 1
          ? response(route, 503, { message: 'Email delivery failed. No active invitation remains.' })
          : response(route, 200, { email: sent.email, status: 'Sent',
              expiresAtUtc: new Date(Date.now() + 48 * 3600000).toISOString() })
      })
      await t.page.getByLabel('Owner Email Address').fill('new-owner@example.test')
      await t.page.getByRole('button', { name: 'Verify Owner' }).click()
      await t.page.getByRole('heading', { name: 'Invite new Owner' }).waitFor()
      await t.page.getByLabel('Reason (audit log)').fill('Approved new store owner invitation')
      await t.page.getByRole('button', { name: 'Send secure email invitation' }).click()
      await t.page.getByRole('alert').filter({ hasText: 'Email delivery failed' }).waitFor()
      assert.equal(await t.page.getByText('Invitation emailed to', { exact: false }).count(), 0)
      await t.page.getByRole('button', { name: 'Send secure email invitation' }).click()
      await t.page.getByText('Invitation emailed to new-owner@example.test', { exact: false }).waitFor()
      assert.equal(inviteAttempt, 2, 'Delivery failure must not show false success')
      emailAccepted = true
      await t.page.getByRole('button', { name: 'Verify Owner' }).click()
      await t.page.getByText('14 days from business creation').waitFor()
      await t.page.getByRole('button', { name: 'Back', exact: true }).click()
      await t.page.getByLabel('Owner Email Address').fill(ownerEmail)
      await t.page.route('**/api/platform/businesses', route => {
        if (route.request().method() !== 'POST') return route.fallback()
        submissions.push(route.request().postDataJSON())
        if (submissions.length === 1) {
          trialDays = 21
          return response(route, 409, {
            code: 'TRIAL_DURATION_CHANGED',
            message: 'The trial duration changed. Review and confirm the updated duration.'
          })
        }
        if (submissions.length === 2) return route.abort('connectionreset')
        return response(route, 200, {
          id: 42, alreadyExists: true, trialEndsAtUtc: '2026-12-07T15:00:00Z'
        })
      })
      await t.page.getByLabel('Owner Email Address').fill(ownerEmail)
      await t.page.getByRole('button', { name: 'Verify Owner' }).click()
      await t.page.getByText('14 days from business creation').waitFor()
      await t.page.getByRole('button', { name: 'Review Business' }).click()
      await t.page.getByText('Starter · 14-day trial').waitFor()
      await t.page.getByRole('checkbox', { name: /checked the business details/ }).check()
      await t.page.getByRole('button', { name: 'Confirm & Create Business' }).click()
      await t.page.getByRole('alert').filter({ hasText: 'The trial duration changed' }).waitFor()
      await t.page.getByText('Starter · 21-day trial').waitFor()
      const confirmation = t.page.getByRole('checkbox', { name: /checked the business details/ })
      assert.equal(await confirmation.isChecked(), false)
      assert.equal(await t.page.getByRole('button', { name: 'Confirm & Create Business' }).isDisabled(), true)
      await confirmation.check()
      await t.page.getByRole('button', { name: 'Confirm & Create Business' }).click()
      await t.page.getByRole('alert').filter({ hasText: 'previous request may have completed' }).waitFor()
      assert.equal(await t.page.getByRole('button', { name: 'Back', exact: true }).isDisabled(), true)
      await t.page.getByRole('button', { name: 'Retry Creation' }).click()
      await t.page.getByRole('heading', { name: 'Business creation confirmed' }).waitFor()
      await t.page.getByText('QA Retail Shop · Business ID 42').waitFor()
      await t.page.getByText(ownerEmail + ' was assigned as Owner.').waitFor()
      await t.page.getByText(/Trial ends .*(8|20):30.*Asia\/Kolkata/).waitFor()
      assert.equal(await t.page.locator('time').getAttribute('datetime'), '2026-12-07T15:00:00Z')
      assert.equal(submissions.length, 3)
      assert.equal(submissions[0].expectedTrialDays, 14)
      assert.equal(submissions[1].expectedTrialDays, 21)
      assert.ok(submissions[1].requestKey)
      assert.deepEqual(submissions[2], submissions[1])
      await t.screenshot('SA-08-provisioning-recovered')
      console.log('PASS SA-08 lost creation response recovers with the same request key')
    }
    await t.check()
    await t.context.close()
    console.log('PASS SA-07 provisioning eligibility block and recovery at ' + width + 'px')
  }

  console.log('PASS all first-screen browser QA scenarios')
} finally {
  await browser.close()
}
