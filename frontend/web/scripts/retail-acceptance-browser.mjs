// Runs only against the isolated CI SQL fixture created by Classified_product_flows_from_purchase_to_stock_and_cash_sale.
// No intercepted or mocked API calls. Never point this mutation test at a merchant host.
import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { chromium } from 'playwright'

const base = 'https://127.0.0.1:5173'
const output = new URL('../qa-acceptance/', import.meta.url)
await mkdir(output, { recursive: true })
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
  for (const width of [360, 768, 1366]) {
    await page.setViewportSize({ width, height: 780 })
    await page.getByRole('heading', { name: 'Transfer #' + transfer.id + ' — Completed' }).waitFor()
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Horizontal page overflow at ' + width)
    await page.screenshot({ path: new URL('transfer-' + width + '.png', output).pathname, fullPage: true })
  }
  assert.deepEqual(errors, [])
  console.log('PASS real HTTPS browser dispatch and receiving, SQL-backed balances and 3 viewport checks')
  await context.close()
} finally {
  await browser.close()
}
