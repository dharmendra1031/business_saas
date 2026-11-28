// Runs only against the isolated CI SQL fixture created by Classified_product_flows_from_purchase_to_stock_and_cash_sale.
// No intercepted or mocked API calls. Never point this mutation test at a merchant host.
import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { chromium } from 'playwright'

const base = 'https://127.0.0.1:5173'
const output = new URL('../qa-acceptance/', import.meta.url)
await mkdir(output, { recursive: true })
const viewportWidths = [360, 768, 1366, 1920]
async function captureResponsive(page, name, ready) {
  for (const width of viewportWidths) {
    await page.setViewportSize({ width, height: 780 })
    await ready.waitFor({ state: 'visible' })
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    await page.screenshot({ path: new URL(name + '-' + width + '.png', output).pathname, fullPage: true })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), name + ' page overflow at ' + width)
  }
}
const browser = await chromium.launch({ headless: true })
try {
  // Only the locally generated, one-day CI certificate is untrusted.
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 360, height: 780 } })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('dialog', dialog => dialog.accept())
  const get = async path => {
    const response = await context.request.get(base + '/api' + path)
    assert.equal(response.status(), 200, path)
    return response.json()
  }
  await page.goto(base)
  await page.getByRole('heading', { name: 'Sign in' }).waitFor()
  await page.getByLabel('Email address').fill('sql-retail-flow-owner@example.test')
  await page.getByLabel('Password').fill('TestPassword123!')
  const login = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
  await page.getByRole('button', { name: /^Sign in/ }).click()
  assert.equal((await login).status(), 204)
  await page.getByRole('button', { name: 'Open navigation' }).waitFor()
  const cookies = await context.cookies()
  assert.ok(cookies.some(c => c.name === 'BusinessSaas.Auth' && c.secure && c.httpOnly))
  const businesses = await get('/businesses')
  assert.equal(businesses.length, 1)
  const business = businesses[0]
  assert.equal(business.name, 'SQL Retail Flow')
  assert.equal(business.roleCode, 'Owner')
  const prefix = '/businesses/' + business.id
  const dashboard = await get(prefix + '/dashboard')
  const source = dashboard.locations.find(l => l.code === 'MAIN')
  const destination = dashboard.locations.find(l => l.code === 'ACC2')
  assert.ok(source && destination)
  const stock = async location => {
    const data = await get(prefix + '/stock/locations/' + location + '?search=SQL-PACK-TEST')
    assert.equal(data.items.length, 1)
    return data.items[0]
  }
  const beforeSource = await stock(source.id)
  const beforeDestination = await stock(destination.id)
  assert.equal(beforeSource.quantity, 2.5)
  assert.equal(beforeDestination.quantity, 1)
  await page.getByRole('button', { name: 'Open navigation' }).click()
  await page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Inventory' }).click()
  await page.getByRole('button', { name: 'Stock Transfer', exact: true }).click()
  await page.getByRole('button', { name: 'New transfer' }).click()
  await page.getByLabel('From location').selectOption(String(source.id))
  await page.getByLabel('To location').selectOption(String(destination.id))
  await page.getByLabel('Search product / scan barcode').fill('SQL-PACK-TEST')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByLabel('Transfer quantity for SQL Pack Test').fill('1')
  await page.getByLabel('Transfer reason').fill('Real browser acceptance transfer')
  const dispatched = page.waitForResponse(r => r.url().endsWith('/stock/transfers') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Approve & dispatch' }).click()
  const dispatchResponse = await dispatched
  assert.equal(dispatchResponse.status(), 200)
  const transfer = await dispatchResponse.json()
  await page.getByRole('heading', { name: 'Transfer #' + transfer.id + ' — InTransit' }).waitFor()
  const path = prefix + '/stock/transfers/' + transfer.id
  assert.equal((await stock(source.id)).quantity, 1.5)
  assert.equal((await stock(destination.id)).quantity, 1)
  const detail = await get(path)
  assert.equal(detail.lines[0].remaining, 1)
  await page.locator('#transfer-line').selectOption(String(detail.lines[0].id))
  await page.locator('#transfer-receive-qty').fill('1')
  const received = page.waitForResponse(r => r.url().endsWith('/stock/transfers/' + transfer.id + '/receive') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Post receiving' }).click()
  assert.equal((await received).status(), 200)
  await page.getByRole('heading', { name: 'Transfer #' + transfer.id + ' — Completed' }).waitFor()
  assert.equal((await stock(source.id)).quantity, 1.5)
  assert.equal((await stock(destination.id)).quantity, 2)
  const completed = await get(path)
  assert.equal(completed.lines[0].remaining, 0)
  assert.equal(completed.lines[0].received, 1)
  await captureResponsive(page, 'transfer', page.getByRole('heading', { name: 'Transfer #' + transfer.id + ' — Completed' }))
  console.log('PASS real HTTPS browser dispatch and receiving, SQL-backed balances and 4 viewport checks')

  // The earlier SQL test closed the cashier's first shift. Start a new one through the real UI.
  const cashierContext = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } })
  try {
    const cashierPage = await cashierContext.newPage()
    cashierPage.on('pageerror', error => errors.push(error.message))
    await cashierPage.goto(base)
    await cashierPage.getByRole('heading', { name: 'Sign in' }).waitFor()
    await cashierPage.getByLabel('Email address').fill('sql-retail-flow-cashier@example.test')
    await cashierPage.getByLabel('Password').fill('TestPassword123!')
    const cashierLogin = cashierPage.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
    await cashierPage.getByRole('button', { name: /^Sign in/ }).click()
    assert.equal((await cashierLogin).status(), 204)
    await cashierPage.getByRole('region', { name: 'Point of Sale checkout' }).waitFor()
    const account = await (await cashierContext.request.get(base + '/api/auth/me')).json()
    const register = cashierPage.getByRole('region', { name: 'Cash register' })
    await register.getByLabel('Opening cash (INR)').fill('100')
    const opening = cashierPage.waitForResponse(r => r.url().endsWith('/cash-register/open') && r.request().method() === 'POST')
    await register.getByRole('button', { name: 'Open Shift' }).click()
    const openingResponse = await opening
    assert.equal(openingResponse.status(), 200)
    const shift = await openingResponse.json()
    assert.equal(shift.actorUserId, account.id)
    assert.equal(shift.locationId, source.id)
    await register.getByText('Shift #' + shift.id + ' open · Opening cash INR 100.00').waitFor()
    await cashierPage.getByLabel('Search product or scan barcode').fill('SQL-PACK-TEST')
    await cashierPage.getByRole('button', { name: 'Add exact barcode or SKU' }).click()
    await cashierPage.getByRole('spinbutton', { name: 'Quantity of SQL Pack Test' }).waitFor()
    await cashierPage.getByRole('button', { name: /Pay \/ Checkout/ }).click()
    const paymentDialog = cashierPage.getByRole('dialog', { name: 'Complete Payment' })
    await paymentDialog.waitFor()
    await captureResponsive(cashierPage, 'cashier-payment', paymentDialog)
    await cashierPage.setViewportSize({ width: 1366, height: 768 })
    await cashierPage.getByLabel('Cash received').fill('10')
    const sale = cashierPage.waitForResponse(r => r.url().endsWith('/cash-sales') && r.request().method() === 'POST')
    await cashierPage.getByRole('button', { name: 'Save Only' }).click()
    const saleResponse = await sale
    assert.equal(saleResponse.status(), 200)
    const receipt = await saleResponse.json()
    assert.equal(receipt.total, 10)
    await cashierPage.getByRole('button', { name: 'New sale' }).waitFor()
    assert.equal((await stock(source.id)).quantity, 0.5)
    assert.equal((await stock(destination.id)).quantity, 2)
    const summaryPath = prefix + '/cash-register/' + shift.id + '/summary?locationId=' + source.id
    const summary = await get(summaryPath)
    assert.equal(summary.openingCash, 100)
    assert.equal(summary.cashSales, 10)
    assert.equal(summary.expectedCash, 110)
    assert.equal((await cashierContext.request.get(base + '/api' + summaryPath)).status(), 404)
    assert.equal((await cashierContext.request.get(base + '/api' + prefix + '/stock/transfers')).status(), 404)
    await cashierPage.screenshot({ path: new URL('cashier-posted-sale.png', output).pathname, fullPage: true })
    await captureResponsive(cashierPage, 'cashier-posted-sale', cashierPage.getByRole('button', { name: 'New sale' }))
    // Close the new cashier shift using the separately authenticated Owner UI.
    await page.locator('.app-sidebar .nav-entry').filter({ hasText: 'Reports' }).click()
    await page.getByRole('tab', { name: 'Day Closing' }).click()
    const shiftRow = page.getByRole('row').filter({ hasText: '#' + shift.id + ' / #' + account.id })
    await shiftRow.getByRole('button', { name: 'Review shift' }).click()
    await page.getByLabel('Actual counted cash').fill('105')
    await page.getByRole('checkbox', { name: /I counted this shift/ }).check()
    assert.equal(await page.getByRole('button', { name: 'Close shift', exact: true }).isEnabled(), false)
    await page.getByLabel('Variance reason (required for differences)').fill('Acceptance counted cash shortage')
    await page.getByRole('checkbox', { name: /I counted this shift/ }).check()
    const closing = page.waitForResponse(r => r.url().endsWith('/cash-register/' + shift.id + '/close') && r.request().method() === 'POST')
    await page.getByRole('button', { name: 'Close shift', exact: true }).click()
    const closingResponse = await closing
    assert.equal(closingResponse.status(), 200)
    const closed = await closingResponse.json()
    assert.equal(closed.closingExpectedCash, 110)
    assert.equal(closed.closingActualCash, 105)
    assert.equal(closed.variance, -5)
    await page.getByText('Shift #' + shift.id + ' closed successfully.').waitFor()
    await shiftRow.getByRole('button', { name: 'View closing' }).click()
    await page.getByText(/Expected INR 110\.00 · Counted INR 105\.00 · Variance INR -5\.00/).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Close shift', exact: true }).count(), 0)
    const history = await get(prefix + '/cash-register?locationId=' + source.id + '&openOnly=false')
    const recorded = history.items.find(item => item.id === shift.id)
    assert.equal(recorded.variance, -5)
    assert.ok(recorded.closedAtUtc)
    await captureResponsive(page, 'closing', page.getByText(/Expected INR 110\.00 · Counted INR 105\.00 · Variance INR -5\.00/))
    console.log('PASS real cashier opening, cash sale, tenant role restrictions and owner closing with shortage')
  } finally {
    await cashierContext.close()
  }
  // A separately authenticated non-member must not gain access by reusing known document IDs.
  const outsiderContext = await browser.newContext({ ignoreHTTPSErrors: true })
  try {
    const outsiderPage = await outsiderContext.newPage()
    outsiderPage.on('pageerror', error => errors.push(error.message))
    await outsiderPage.goto(base)
    await outsiderPage.getByRole('heading', { name: 'Sign in' }).waitFor()
    await outsiderPage.getByLabel('Email address').fill('sql-retail-flow-stranger@example.test')
    await outsiderPage.getByLabel('Password').fill('TestPassword123!')
    const outsiderLogin = outsiderPage.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
    await outsiderPage.getByRole('button', { name: /^Sign in/ }).click()
    assert.equal((await outsiderLogin).status(), 204)
    const memberList = await outsiderContext.request.get(base + '/api/businesses')
    assert.equal(memberList.status(), 200)
    assert.ok(!(await memberList.json()).some(item => item.id === business.id))
    for (const restricted of [prefix + '/stock/locations/' + source.id, path, prefix + '/stock/transfers']) {
      assert.equal((await outsiderContext.request.get(base + '/api' + restricted)).status(), 404, restricted)
    }
    console.log('PASS real authenticated non-member denied access to known retail stock and transfer IDs')
  } finally {
    await outsiderContext.close()
  }
  assert.deepEqual(errors, [])
  await context.close()
} finally {
  await browser.close()
}
