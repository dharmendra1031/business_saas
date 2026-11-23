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
        categories: [{ id: 1, name: 'General' }, { id: 2, name: 'Clothing' }],
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
  return { page, context, state, screenshot, check, login, get businessRequests() { return businessRequests }, stockPaths, stockQueries }
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
    assert.equal(await t.page.locator('.business-kpi-card').count(), 1,
      'Unsupported sales and stock KPIs should not consume dashboard space')
    assert.equal(await t.page.locator('.business-kpi-card strong').first().innerText(), '3')
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
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-supplier option')].some(option =>
        option.textContent?.includes('Wholesaler')))
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-product option')].some(option =>
        option.textContent?.includes('Blue Shirt')))
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
    await t.page.locator('#purchase-supplier').selectOption('21')
    await t.page.locator('#purchase-product').selectOption('11')
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).fill('4')
    await t.page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await t.page.getByRole('heading', { name: 'New purchase order', level: 3 }).waitFor({ state: 'hidden' })
    await t.page.getByRole('button', { name: 'New Purchase Order' }).click()
    await t.page.getByRole('heading', { name: 'New purchase order', level: 3 }).waitFor()
    assert.equal(await t.page.locator('#purchase-supplier').inputValue(), '0',
      'Canceled order must not keep the previous supplier')
    assert.equal(await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).count(), 0,
      'Canceled order must not keep previously selected product lines')
    await t.page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await t.check()
    await t.context.close()
    console.log('PASS Purchase catalogs load on demand; canceled drafts reopen empty')
  }

  for (const retryPosting of [false, true]) {
    // The same UI path must work normally and after a draft was saved but posting failed.
    const t = await scenario(retryPosting ? 'purchase-post-recovery' : 'purchase-to-stock-browser-flow', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
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
        order = { id: 71, status: 'Draft', quantity: 2 }
        return response(route, 201, { id: 71, status: 'Draft' })
      }
      if (path.endsWith('/purchase-orders/71') && method === 'GET') {
        const posted = receiving?.status === 'Posted' ? receiving.quantity : 0
        return response(route, 200, { id: 71, locationId: 1, supplierId: 21,
          status: order.status, createdAtUtc: '2026-11-23T02:00:00Z', approvedAtUtc: null,
          lines: [{ id: 72, productId: 11, name: 'Blue Shirt', sku: 'BLUE-11',
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
        stock += receiving.quantity
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
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-supplier option')].some(option => option.value === '21'))
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-product option')].some(option => option.value === '11'))
    await t.page.locator('#purchase-supplier').selectOption('21')
    await t.page.locator('#purchase-product').selectOption('11')
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt quantity' }).fill('2')
    await t.page.getByRole('spinbutton', { name: 'Blue Shirt unit cost' }).fill('40')
    assert.equal(await t.page.getByRole('button', { name: 'Save Draft', exact: true }).count(), 1,
      'Owner can still save unfinished purchase orders as Draft')
    await t.page.getByRole('button', { name: 'Save & Approve' }).click()
    await t.page.getByRole('heading', { name: 'Purchase order #71' }).waitFor()
    await t.page.getByRole('button', { name: 'Receive goods' }).waitFor()
    assert.equal(order.status, 'Approved', 'Owner save must approve without a second click')
    assert.equal(stock, 0, 'Approval must not update stock')
    assert.equal(await t.page.getByRole('button', { name: 'Approve purchase order' }).count(), 0)
    await t.page.getByRole('button', { name: 'Receive goods' }).click()
    await t.page.getByRole('button', { name: 'Receive all remaining' }).click()
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
    await t.page.getByText('Blue Shirt · 2.000', { exact: true }).waitFor()
    assert.equal(await t.page.locator('tbody tr').filter({ hasText: '#91' })
      .getByRole('button', { name: 'Post stock' }).count(), 0,
      'Posted receipts must not offer another stock posting')
    assert.equal(receivingCreates, 1, 'Retry after a post failure must reuse the same saved draft')
    assert.equal(await t.page.getByRole('button', { name: 'Review and post' }).count(), 0,
      'Do not reopen a redundant confirmation step after posting')
    assert.equal(receivingPosts, 1, 'Stock posting must only submit once')
    assert.equal(order.status, 'Closed')
    assert.equal(stock, 2)
    await t.page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
    const stockRow = t.page.locator('.retail-stock-list tbody tr').filter({ hasText: 'Blue Shirt' })
    await stockRow.getByText('2.000').waitFor()
    assert.equal(await stockRow.locator('td').nth(2).innerText(), '2.000')
    await t.check()
    await t.context.close()
    console.log(retryPosting
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
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-supplier option')].some(option => option.value === '21'))
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-product option')].some(option => option.value === '11'))
    await t.page.locator('#purchase-supplier').selectOption('21')
    await t.page.locator('#purchase-product').selectOption('11')
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
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-supplier option')].some(option => option.value === '21'))
    await t.page.waitForFunction(() =>
      [...document.querySelectorAll('#purchase-product option')].some(option => option.value === '11'))
    await t.page.locator('#purchase-supplier').selectOption('21')
    await t.page.locator('#purchase-product').selectOption('11')
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
    assert.equal(await t.page.locator('.business-kpi-card').count(), 3,
      'Only verified cash and draft-order metrics should be shown')
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
    const t = await scenario('pos-fractional-base-unit-checkout', {
      role: 'Cashier', hasProducts: true
    })
    let posted = null
    let posts = 0
    await t.page.route('**/api/businesses/1/cash-sales/products*', route =>
      response(route, 200, [{
        id: 91, name: 'Loose Rice', sku: 'RICE-KG', barcode: null,
        salePrice: 80, available: 0.625, unitCode: 'KG'
      }]))
    await t.page.route('**/api/businesses/1/cash-sales', route => {
      if (route.request().method() !== 'POST')
        return response(route, 404, { message: 'Unexpected cash sale route' })
      posted = route.request().postDataJSON()
      posts++
      return response(route, 200, {
        id: 501, total: 10, tendered: 10, currencyCode: 'INR',
        postedAtUtc: '2026-11-23T06:00:00Z',
        lines: [{ id: 502, productName: 'Loose Rice', unitPrice: 80, quantity: 0.125 }]
      })
    })
    await t.login()
    await t.page.locator('.pos-checkout').waitFor()
    await t.page.getByRole('button', { name: /Add Loose Rice/ }).waitFor()
    await t.page.getByRole('button', { name: /Add Loose Rice/ }).click()
    await t.page.getByRole('spinbutton', { name: 'Quantity of Loose Rice' }).fill('0.125')
    assert.equal(await t.page.getByRole('spinbutton', { name: 'Quantity of Loose Rice' }).inputValue(),
      '0.125')
    await t.page.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    await t.page.getByRole('dialog', { name: 'Complete Payment' }).waitFor()
    await t.page.getByLabel('Cash received').fill('10')
    await t.page.getByRole('button', { name: 'Confirm Cash Payment' }).click()
    await t.page.getByRole('heading', { name: 'Cash receipt #501' }).waitFor()
    assert.equal(posts, 1, 'Fractional POS must post once')
    assert.equal(posted.lines.length, 1)
    assert.equal(posted.lines[0].productId, 91)
    assert.equal(posted.lines[0].quantity, 0.125)
    assert.equal(posted.tendered, 10)
    await t.check()
    await t.context.close()
    console.log('PASS POS sells fractional KG in base units with currency rounding')
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
    const alertCard = t.page.locator('.business-kpi-card').filter({ hasText: 'Stock Alerts' })
    assert.equal(await alertCard.locator('strong').innerText(), '2')
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

  {
    const t = await scenario('product-inline-category-unit-brand', {
      role: 'Owner', hasProducts: true, productsFixture: true
    })
    const categories = [
      { id: 1, name: 'General', parentId: null },
      { id: 2, name: 'Clothing', parentId: null }
    ]
    const units = [{ id: 1, name: 'Item', code: 'ITEM' }]
    const brands = []
    let savedProduct = null
    await t.page.route('**/api/businesses/1/products/options', route =>
      response(route, 200, { categories, units, brands }))
    await t.page.route('**/api/businesses/1/categories', route => {
      if (route.request().method() !== 'POST') return route.fallback()
      const body = route.request().postDataJSON()
      assert.equal(body.name, 'Shirts')
      assert.equal(body.parentId, 2)
      categories.push({ id: 31, name: body.name, parentId: body.parentId })
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

    await t.page.getByRole('button', { name: '+ Add category' }).click()
    await t.page.locator('#inline-master-name').fill('Shirts')
    await t.page.locator('#inline-master-parent').selectOption('2')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() =>
      document.querySelector('#product-category')?.value === '31')
    assert.equal(await t.page.locator('#product-category option:checked').innerText(), 'Clothing / Shirts')

    await t.page.getByRole('button', { name: '+ Add unit' }).click()
    await t.page.locator('#inline-master-name').fill('Box')
    await t.page.locator('#inline-master-code').fill('BOX')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() => document.querySelector('#product-unit')?.value === '32')

    await t.page.getByRole('button', { name: '+ Add brand' }).click()
    await t.page.locator('#inline-master-name').fill('Raymond')
    await t.page.getByRole('button', { name: 'Add & select' }).click()
    await t.page.waitForFunction(() => document.querySelector('#product-brand')?.value === '33')
    await t.page.getByRole('button', { name: 'Save product' }).click()
    await t.page.getByText('Product added.', { exact: false }).waitFor()
    assert.equal(savedProduct.name, 'Cotton Shirt')
    assert.equal(savedProduct.categoryId, 31)
    assert.equal(savedProduct.unitId, 32)
    assert.equal(savedProduct.brandId, 33)
    assert.equal(savedProduct.salePrice, 1299)
    await t.check()
    await t.context.close()
    console.log('PASS Product inline category, unit and brand creation without leaving form')
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
    const t = await scenario('business-owner-all-tabs', { role: 'Owner', hasProducts: true })
    await t.login()
    await t.page.getByRole('heading', { name: 'Business Overview' }).waitFor()
    const modules = [
      ['Products', 'Products', 'BUS-06-products-compact'],
      ['Purchase Orders', 'Purchase Orders', 'BUS-20-purchases-compact'],
      ['Inventory', 'Inventory', 'BUS-27-inventory-compact'],
      ['Product Masters', 'Product Masters', null],
      ['Suppliers', 'Suppliers', null],
      ['Customers', 'Customers', null],
      ['Sales & Returns', 'Sales & Returns', 'BUS-35-sales-preview-compact'],
      ['Cash Register', 'Cash Register', null],
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
      if (shot) await t.screenshot(shot)
    }
    await t.check()
    await t.context.close()
    console.log('PASS business owner live/preview tabs keep one compact title')
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
    await t.screenshot('BUS-06-products-list-desktop')
    await t.page.locator('#product-filter-status').selectOption('inactive')
    await t.page.waitForFunction(() => {
      const rows = document.querySelectorAll('.business-record-table tbody tr')
      return rows.length === 1 && rows[0].textContent.includes('Red Bag')
    })
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 1,
      'Inactive status must filter actual records')
    await t.page.locator('#product-filter-status').selectOption('all')
    await t.page.locator('#product-filter-category').selectOption('2')
    await t.page.waitForFunction(() => {
      const rows = document.querySelectorAll('.business-record-table tbody tr')
      return rows.length === 1 && rows[0].textContent.includes('Blue Shirt')
    })
    assert.equal(await t.page.locator('.business-record-table tbody tr').count(), 1,
      'Category selection must filter records')
    await t.page.locator('#product-filter-category').selectOption('0')
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

  console.log('PASS all first-screen browser QA scenarios')
} finally {
  await browser.close()
}
